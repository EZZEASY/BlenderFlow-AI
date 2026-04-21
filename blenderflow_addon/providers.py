"""AI 3D generation provider registry.

Each provider follows a uniform create → poll → import lifecycle. HTTP work
runs on a background thread; import steps always hop back to Blender's main
thread via bpy.app.timers.register.

Providers bundled in this module:
  - Hunyuan3D (Tencent Cloud OFFICIAL_API mode) — China-accessible without
    a VPN. Uses the TC3-HMAC-SHA256 signature scheme.

The existing Tripo provider keeps its implementation in the C# plugin for
now; this file only defines providers that run Python-side. Future ports
(Hyper3D Rodin, Tripo-to-Python) plug in here following the same shape.

Signature-code credit: adapted from the blender-mcp project by
Siddharth Ahuja (github.com/ahujasid/blender-mcp), MIT-licensed.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import re
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Callable, Optional

import bpy


# ─── Shared types ──────────────────────────────────────────────────────


@dataclass
class JobHandle:
    """Identifier + provider-specific bookkeeping for an in-flight job."""
    provider: str
    job_id: str
    submitted_at: float = field(default_factory=time.time)
    meta: dict = field(default_factory=dict)


class ProviderError(Exception):
    """Raised when an upstream provider responds with a recoverable error.
    The error message is shown to the user in Blender's error dialog.
    """


# ─── Base class ────────────────────────────────────────────────────────


class AIProvider:
    """Common interface all provider implementations expose."""

    name: str = "unknown"
    needs_prompt: bool = True   # most text-to-3D providers
    needs_image: bool = False

    def __init__(self, config: dict):
        self.config = config or {}

    def test_connection(self) -> tuple[bool, str]:
        """Return ``(ok, message)``. Used by the "Test connection" button."""
        raise NotImplementedError

    def generate(self, prompt: Optional[str], images: Optional[list[str]] = None) -> JobHandle:
        """Submit a job. HTTP-blocking call, intended for a worker thread."""
        raise NotImplementedError

    def poll(self, handle: JobHandle) -> dict:
        """Return ``{state, progress, model_url?, error?}``.

        ``state`` is one of ``queued | running | done | failed``. ``progress``
        is in [0, 1].
        """
        raise NotImplementedError

    def download_and_import(self, handle: JobHandle, object_name: str) -> str:
        """Fetch the generated asset and import it. Called on MAIN thread.
        Returns the imported object's name in the scene.
        """
        raise NotImplementedError

    def cancel(self, handle: JobHandle) -> None:
        """Best-effort cancel. Default no-op; override if the provider
        exposes a cancel endpoint."""
        return None


# ─── Hunyuan3D (Tencent Cloud OFFICIAL) ────────────────────────────────


class HunyuanProvider(AIProvider):
    """Tencent Cloud Hunyuan3D — the only provider here that's reachable
    from mainland China without a VPN.

    Uses the v3 signature (TC3-HMAC-SHA256). Two sub-modes:
      - "PRO"   — richer output, text OR image (not both)
      - "RAPID" — faster, requires an image
    """

    name = "hunyuan3d"
    API_HOST = "ai3d.tencentcloudapi.com"
    API_VERSION = "2025-05-13"
    REGION = "ap-guangzhou"

    def __init__(self, config):
        super().__init__(config)
        self.secret_id = config.get("secret_id", "")
        self.secret_key = config.get("secret_key", "")
        self.api_type = (config.get("api_type") or "PRO").upper()

    # ─── Tencent Cloud v3 signature ─────────────────────────────────

    def _sign_headers(self, action: str, payload: str) -> dict:
        """Build the Authorization header per Tencent Cloud's v3 spec."""
        now = int(time.time())
        date = datetime.fromtimestamp(now, tz=timezone.utc).strftime("%Y-%m-%d")

        # 1. Canonical request
        hashed_body = hashlib.sha256(payload.encode("utf-8")).hexdigest()
        canonical_headers = (
            "content-type:application/json; charset=utf-8\n"
            f"host:{self.API_HOST}\n"
            f"x-tc-action:{action.lower()}\n"
        )
        signed_headers = "content-type;host;x-tc-action"
        canonical_request = (
            f"POST\n/\n\n{canonical_headers}\n{signed_headers}\n{hashed_body}"
        )

        # 2. String to sign
        credential_scope = f"{date}/ai3d/tc3_request"
        hashed_canonical = hashlib.sha256(canonical_request.encode("utf-8")).hexdigest()
        string_to_sign = (
            f"TC3-HMAC-SHA256\n{now}\n{credential_scope}\n{hashed_canonical}"
        )

        # 3. Derived signing key
        def _hmac(key: bytes, msg: str) -> bytes:
            return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()

        secret_date = _hmac(("TC3" + self.secret_key).encode("utf-8"), date)
        secret_service = _hmac(secret_date, "ai3d")
        secret_signing = _hmac(secret_service, "tc3_request")
        signature = hmac.new(
            secret_signing, string_to_sign.encode("utf-8"), hashlib.sha256
        ).hexdigest()

        # 4. Authorization header
        authorization = (
            f"TC3-HMAC-SHA256 Credential={self.secret_id}/{credential_scope}, "
            f"SignedHeaders={signed_headers}, Signature={signature}"
        )
        return {
            "Authorization": authorization,
            "Content-Type": "application/json; charset=utf-8",
            "Host": self.API_HOST,
            "X-TC-Action": action,
            "X-TC-Timestamp": str(now),
            "X-TC-Version": self.API_VERSION,
            "X-TC-Region": self.REGION,
        }

    def _tc_call(self, action: str, body: dict, timeout: float = 20.0) -> dict:
        """POST a signed request, return parsed JSON."""
        payload = json.dumps(body, separators=(",", ":"), ensure_ascii=True)
        headers = self._sign_headers(action, payload)
        req = urllib.request.Request(
            f"https://{self.API_HOST}/",
            data=payload.encode("utf-8"),
            headers=headers,
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            body_text = e.read().decode("utf-8", errors="replace")[:400]
            raise ProviderError(
                f"Tencent API HTTP {e.code}: {body_text}"
            ) from e
        except urllib.error.URLError as e:
            raise ProviderError(f"Tencent API network error: {e}") from e

    # ─── AIProvider implementation ──────────────────────────────────

    def test_connection(self):
        if not self.secret_id or not self.secret_key:
            return False, "SecretId or SecretKey is empty"
        try:
            # Submit a "dry" query with a bogus JobId to probe auth.
            # Tencent will return AuthFailure / InvalidParameter — either
            # answer confirms that our signature reached the server.
            resp = self._tc_call(
                self._query_action(),
                {"JobId": "00000000-0000-0000-0000-000000000000"},
                timeout=10,
            )
            err = resp.get("Response", {}).get("Error")
            if err and err.get("Code", "").startswith("AuthFailure"):
                return False, f"Credentials rejected: {err.get('Code')}"
            # Any other response means auth worked.
            return True, "✓ Tencent Cloud credentials accepted"
        except ProviderError as e:
            msg = str(e)
            if "AuthFailure" in msg:
                return False, "Credentials rejected (AuthFailure)"
            return False, msg

    def _submit_action(self) -> str:
        return (
            "SubmitHunyuanTo3DProJob"
            if self.api_type == "PRO"
            else "SubmitHunyuanTo3DRapidJob"
        )

    def _query_action(self) -> str:
        return (
            "QueryHunyuanTo3DProJob"
            if self.api_type == "PRO"
            else "QueryHunyuanTo3DRapidJob"
        )

    def generate(self, prompt, images=None):
        if not self.secret_id or not self.secret_key:
            raise ProviderError(
                "Tencent SecretId/SecretKey are empty — open BlenderFlow settings."
            )

        body: dict = {}
        if self.api_type == "PRO":
            # PRO accepts prompt OR image — not both.
            if prompt and images:
                raise ProviderError(
                    "Hunyuan3D PRO accepts either a prompt or an image, not both."
                )
            if prompt:
                body["Prompt"] = prompt[:1024]
            elif images:
                body.update(self._encode_image(images[0]))
            else:
                raise ProviderError("Hunyuan3D PRO needs a prompt or an image.")
        else:  # RAPID
            if not images:
                raise ProviderError("Hunyuan3D RAPID requires an image.")
            body.update(self._encode_image(images[0]))

        resp = self._tc_call(self._submit_action(), body, timeout=30)
        job_id = resp.get("Response", {}).get("JobId")
        if not job_id:
            err = resp.get("Response", {}).get("Error", {})
            raise ProviderError(
                f"Tencent did not return a JobId: {err.get('Message', resp)}"
            )
        return JobHandle(self.name, job_id, meta={"api_type": self.api_type})

    def _encode_image(self, image: str) -> dict:
        """Accept either an http(s) URL or a local path; Tencent accepts both."""
        if re.match(r"^https?://", image, re.IGNORECASE):
            return {"ImageUrl": image}
        if not os.path.isfile(image):
            raise ProviderError(f"Image not found: {image}")
        with open(image, "rb") as f:
            return {"ImageBase64": base64.b64encode(f.read()).decode("ascii")}

    def poll(self, handle):
        resp = self._tc_call(
            self._query_action(),
            {"JobId": handle.job_id},
            timeout=15,
        )
        data = resp.get("Response", {})
        if "Error" in data:
            err = data["Error"]
            raise ProviderError(
                f"Tencent error {err.get('Code')}: {err.get('Message')}"
            )

        status = (data.get("Status") or "").upper()
        if status == "DONE":
            files = data.get("ResultFile3Ds") or []
            model_url = files[0].get("Url") if files else None
            if not model_url:
                return {"state": "failed", "error": "No result URL in DONE response"}
            return {"state": "done", "progress": 1.0, "model_url": model_url}
        if status == "FAIL":
            return {
                "state": "failed",
                "error": data.get("ErrorMessage")
                or data.get("ErrorCode")
                or "Tencent reported FAIL",
            }
        # WAIT / RUN — still progressing. Tencent doesn't give a numeric %,
        # so we fudge based on elapsed time (most jobs finish in <60s).
        elapsed = time.time() - handle.submitted_at
        progress = min(0.9, elapsed / 60.0)
        return {"state": "running", "progress": progress}

    def download_and_import(self, handle, object_name):
        # Re-poll so we have the freshest URL (Tencent signed URLs expire).
        status = self.poll(handle)
        model_url = status.get("model_url")
        if not model_url:
            raise ProviderError("No result URL available")

        tmp_dir = tempfile.mkdtemp(prefix="blenderflow_hunyuan_")
        zip_path = os.path.join(tmp_dir, "asset.zip")
        _download_to_file(model_url, zip_path)

        # Tencent ships an OBJ + MTL + textures inside the zip.
        with zipfile.ZipFile(zip_path) as z:
            _safe_extract(z, tmp_dir)

        obj_files = [
            os.path.join(tmp_dir, f)
            for f in os.listdir(tmp_dir)
            if f.lower().endswith(".obj")
        ]
        if not obj_files:
            raise ProviderError("No .obj in the downloaded archive")

        obj_path = obj_files[0]
        before = set(bpy.data.objects)
        try:
            bpy.ops.wm.obj_import(filepath=obj_path)
        except AttributeError:
            # Blender < 4.0
            bpy.ops.import_scene.obj(filepath=obj_path)
        added = [o for o in bpy.data.objects if o not in before]
        if not added:
            raise ProviderError("OBJ import produced no objects")
        added[0].name = object_name
        return added[0].name


# ─── Hyper3D Rodin (MAIN_SITE) ─────────────────────────────────────────


# Blender-MCP's published free-trial Bearer token — credit: Siddharth Ahuja's
# blender-mcp project. Rate-limited and shared, but lets first-time users
# generate a model without signing up.
RODIN_FREE_TRIAL_KEY = "k9TcfFoEhNd9cCPP2guHAHHHkctZHIRhZDywZ1euGUXwihbYLpOjQhofby80NJez"


class HyperRodinProvider(AIProvider):
    """Hyper3D Rodin — text/image-to-3D via hyperhuman.deemos.com.

    MAIN_SITE mode only for now. Multi-stage lifecycle:
        POST /api/v2/rodin     (multipart) → {uuid, jobs: {subscription_key}}
        POST /api/v2/status    {subscription_key} → {jobs: [{status: ...}, ...]}
        POST /api/v2/download  {task_uuid}      → {list: [{name, url}, ...]}
        GET  <cdn url>                          → GLB bytes
    """

    name = "hyperrodin"
    BASE = "https://hyperhuman.deemos.com/api/v2"

    def __init__(self, config):
        super().__init__(config)
        self.api_key = config.get("api_key", "") or RODIN_FREE_TRIAL_KEY
        self.tier = config.get("tier", "Sketch")
        self.mesh_mode = config.get("mesh_mode", "Raw")

    def _auth_headers(self) -> dict:
        return {"Authorization": f"Bearer {self.api_key}"}

    def _post_json(self, url: str, body: dict, timeout: float = 30.0) -> dict:
        data = json.dumps(body).encode("utf-8")
        req = urllib.request.Request(
            url,
            data=data,
            headers={**self._auth_headers(), "Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            body_text = e.read().decode("utf-8", errors="replace")[:400]
            raise ProviderError(_hyper3d_http_msg(e.code, body_text)) from e
        except urllib.error.URLError as e:
            raise ProviderError(f"Hyper3D network error: {e}") from e

    def test_connection(self):
        if not self.api_key:
            return False, "API key is empty"
        # The status endpoint accepts any subscription key; an AuthFailure would
        # surface in the HTTP layer, while a nonsense key just gets an empty
        # jobs list — either proves the bearer token is accepted.
        try:
            resp = self._post_json(
                f"{self.BASE}/status",
                {"subscription_key": "blenderflow_connectivity_test"},
                timeout=10,
            )
            if "error" in resp:
                return False, f"Hyper3D: {resp['error']}"
            return True, "✓ Hyper3D Rodin credentials accepted"
        except ProviderError as e:
            msg = str(e)
            if "401" in msg or "403" in msg:
                return False, "Key rejected (401/403)"
            return False, msg

    def generate(self, prompt, images=None):
        # Multipart form data — we hand-roll because urllib doesn't have a
        # built-in encoder and we don't depend on requests.
        fields: list[tuple[str, object]] = [
            ("tier", self.tier),
            ("mesh_mode", self.mesh_mode),
        ]
        if prompt:
            fields.append(("prompt", prompt))
        if images:
            for i, path in enumerate(images):
                if not os.path.isfile(path):
                    raise ProviderError(f"Image not found: {path}")
                with open(path, "rb") as f:
                    data = f.read()
                suffix = os.path.splitext(path)[1] or ".png"
                fields.append(
                    (
                        "images",
                        (f"{i:04d}{suffix}", data, "application/octet-stream"),
                    )
                )

        body, content_type = _multipart_encode(fields)
        req = urllib.request.Request(
            f"{self.BASE}/rodin",
            data=body,
            headers={**self._auth_headers(), "Content-Type": content_type},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                resp = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            body_text = e.read().decode("utf-8", errors="replace")[:400]
            raise ProviderError(_hyper3d_http_msg(e.code, body_text)) from e
        except urllib.error.URLError as e:
            raise ProviderError(f"Hyper3D network error: {e}") from e

        if "error" in resp:
            raise ProviderError(_hyper3d_api_error_msg(resp.get("error", ""), self.api_key))
        task_uuid = resp.get("uuid")
        subscription_key = (resp.get("jobs") or {}).get("subscription_key")
        if not task_uuid or not subscription_key:
            raise ProviderError(f"Hyper3D gave an unexpected response: {resp}")
        return JobHandle(
            self.name,
            task_uuid,
            meta={"subscription_key": subscription_key},
        )

    def poll(self, handle):
        resp = self._post_json(
            f"{self.BASE}/status",
            {"subscription_key": handle.meta["subscription_key"]},
            timeout=15,
        )
        if "error" in resp:
            raise ProviderError(_hyper3d_api_error_msg(resp.get("error", ""), self.api_key))
        statuses = [job.get("status", "") for job in (resp.get("jobs") or [])]
        if not statuses:
            return {"state": "queued", "progress": 0.0}
        if any(s == "Failed" for s in statuses):
            return {"state": "failed", "error": "Hyper3D reported Failed"}
        if all(s == "Done" for s in statuses):
            return {"state": "done", "progress": 1.0}
        done = sum(1 for s in statuses if s == "Done")
        return {"state": "running", "progress": done / len(statuses) * 0.9}

    def download_and_import(self, handle, object_name):
        resp = self._post_json(
            f"{self.BASE}/download",
            {"task_uuid": handle.job_id},
            timeout=30,
        )
        if "error" in resp:
            raise ProviderError(_hyper3d_api_error_msg(resp.get("error", ""), self.api_key))
        glb_url = None
        for entry in resp.get("list") or []:
            if entry.get("name", "").lower().endswith(".glb"):
                glb_url = entry.get("url")
                break
        if not glb_url:
            raise ProviderError("Hyper3D response did not include a .glb URL")

        tmp_dir = tempfile.mkdtemp(prefix="blenderflow_hyper3d_")
        glb_path = os.path.join(tmp_dir, f"{object_name}.glb")
        _download_to_file(glb_url, glb_path)

        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=glb_path)
        added = [o for o in bpy.data.objects if o not in before]
        if not added:
            raise ProviderError("glTF import produced no objects")
        # glTF often wraps the mesh in an empty — rename the first mesh child
        # rather than the wrapper so the name is meaningful.
        target = next((o for o in added if o.type == "MESH"), added[0])
        target.name = object_name
        return target.name


def _multipart_encode(fields) -> tuple[bytes, str]:
    """Build a multipart/form-data body. ``fields`` is a list of
    ``(name, value_or_file_tuple)`` where the file tuple is
    ``(filename, bytes_content, content_type)``.
    """
    boundary = "----BlenderFlow" + uuid.uuid4().hex
    body = bytearray()
    for name, value in fields:
        body.extend(f"--{boundary}\r\n".encode("utf-8"))
        if isinstance(value, tuple):
            filename, content, content_type = value
            body.extend(
                (
                    f'Content-Disposition: form-data; name="{name}"; '
                    f'filename="{filename}"\r\n'
                    f"Content-Type: {content_type}\r\n\r\n"
                ).encode("utf-8")
            )
            body.extend(content if isinstance(content, (bytes, bytearray)) else content.encode("utf-8"))
            body.extend(b"\r\n")
        else:
            body.extend(
                f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode("utf-8")
            )
            body.extend(str(value).encode("utf-8"))
            body.extend(b"\r\n")
    body.extend(f"--{boundary}--\r\n".encode("utf-8"))
    return bytes(body), f"multipart/form-data; boundary={boundary}"


# ─── Error-message humanizers ──────────────────────────────────────────


def _hyper3d_http_msg(code: int, body_text: str) -> str:
    """Turn a raw HTTP failure into a user-actionable line."""
    if code == 429:
        return (
            "Hyper3D rate limit hit (HTTP 429). The shared free-trial key is "
            "heavily rate-limited — get your own key at hyper3d.ai for "
            f"sustained use. Server said: {body_text}"
        )
    if code in (401, 403):
        return (
            f"Hyper3D rejected the API key (HTTP {code}). Check it for "
            f"typos in BlenderFlow preferences. Server said: {body_text}"
        )
    if code == 402:
        return (
            "Hyper3D: out of credits (HTTP 402). Top up on hyper3d.ai or "
            f"switch to another provider. Server said: {body_text}"
        )
    return f"Hyper3D HTTP {code}: {body_text}"


def _hyper3d_api_error_msg(err: str, api_key: str) -> str:
    """Turn a 200-OK JSON ``{"error": "..."}`` into a user-actionable line."""
    low = (err or "").lower()
    using_trial = api_key == RODIN_FREE_TRIAL_KEY
    if any(kw in low for kw in ("quota", "limit", "credit", "balance")):
        if using_trial:
            return (
                f"Hyper3D trial quota exhausted: {err}. The shared key is "
                "rate-limited across all users — grab your own at hyper3d.ai."
            )
        return (
            f"Hyper3D quota/credits exhausted: {err}. Top up on hyper3d.ai "
            "or switch to another provider."
        )
    if any(kw in low for kw in ("auth", "key", "token", "forbidden")):
        return f"Hyper3D auth error: {err}. Check your API key."
    return f"Hyper3D: {err}"


# ─── Helpers shared across providers ───────────────────────────────────


def _download_to_file(url: str, dst: str, timeout: float = 120.0) -> None:
    req = urllib.request.Request(url, headers={"User-Agent": "BlenderFlow/1.0"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp, open(dst, "wb") as out:
            while True:
                chunk = resp.read(65536)
                if not chunk:
                    break
                out.write(chunk)
    except urllib.error.URLError as e:
        raise ProviderError(f"Download failed: {e}") from e


def _safe_extract(zf: zipfile.ZipFile, dst_dir: str) -> None:
    """zip-slip guard: reject entries that escape the destination."""
    dst_abs = os.path.abspath(dst_dir)
    for member in zf.namelist():
        target = os.path.abspath(os.path.join(dst_dir, member))
        if not target.startswith(dst_abs + os.sep) and target != dst_abs:
            raise ProviderError(f"Rejected zip entry (path traversal): {member}")
    zf.extractall(dst_dir)


# ─── Registry ──────────────────────────────────────────────────────────


PROVIDERS: dict[str, type[AIProvider]] = {
    "hunyuan3d": HunyuanProvider,
    "hyperrodin": HyperRodinProvider,
}


def build(provider_name: str, config: dict) -> AIProvider:
    cls = PROVIDERS.get(provider_name)
    if cls is None:
        raise ProviderError(f"Unknown provider: {provider_name}")
    return cls(config)
