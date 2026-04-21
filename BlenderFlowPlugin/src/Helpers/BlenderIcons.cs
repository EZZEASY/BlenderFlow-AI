namespace Loupedeck.BlenderFlowPlugin
{
    using System;

    // Flat, geometric icons in Blender's visual language — isometric cube,
    // vertex dots, brush, sparkles. Everything is drawn from primitives so
    // the icons stay sharp at any console resolution (50 / 80 / 116 px).
    internal static class BlenderIcons
    {
        private const Single IsoCos = 0.8660254f; // cos(30°)

        // ─── Isometric cube ──────────────────────────────────────────────

        public static void IsoCube(BitmapBuilder b, Single cx, Single cy, Single r,
            BitmapColor lineColor, Single thickness)
        {
            Single rx = r * IsoCos;
            Single ry = r * 0.5f;
            Single tX = cx,       tY = cy - r;
            Single ltX = cx - rx, ltY = cy - ry;
            Single rtX = cx + rx, rtY = cy - ry;
            Single centerX = cx,  centerY = cy;
            Single lbX = cx - rx, lbY = cy + ry;
            Single rbX = cx + rx, rbY = cy + ry;
            Single btX = cx,      btY = cy + r;

            // top rhombus
            b.DrawLine(tX,  tY,  ltX, ltY, lineColor, thickness);
            b.DrawLine(tX,  tY,  rtX, rtY, lineColor, thickness);
            b.DrawLine(ltX, ltY, centerX, centerY, lineColor, thickness);
            b.DrawLine(rtX, rtY, centerX, centerY, lineColor, thickness);
            // vertical edges
            b.DrawLine(ltX, ltY, lbX, lbY, lineColor, thickness);
            b.DrawLine(rtX, rtY, rbX, rbY, lineColor, thickness);
            b.DrawLine(centerX, centerY, btX, btY, lineColor, thickness);
            // bottom
            b.DrawLine(lbX, lbY, btX, btY, lineColor, thickness);
            b.DrawLine(rbX, rbY, btX, btY, lineColor, thickness);
        }

        // Faint three-face shading (optional, for Object Mode active state)
        public static void IsoCubeFaces(BitmapBuilder b, Single cx, Single cy, Single r,
            BitmapColor topFace, BitmapColor leftFace, BitmapColor rightFace)
        {
            Single rx = r * IsoCos;
            Single ry = r * 0.5f;
            // Top face quads, drawn as overlapping horizontal lines
            FillQuad(b, cx, cy - r, cx + rx, cy - ry, cx, cy, cx - rx, cy - ry, topFace);
            FillQuad(b, cx - rx, cy - ry, cx, cy, cx, cy + r, cx - rx, cy + ry, leftFace);
            FillQuad(b, cx, cy, cx + rx, cy - ry, cx + rx, cy + ry, cx, cy + r, rightFace);
        }

        // Naive quad fill using horizontal scanlines. Vertices given in
        // order p1→p2→p3→p4 forming a convex quad.
        private static void FillQuad(BitmapBuilder b,
            Single p1x, Single p1y, Single p2x, Single p2y,
            Single p3x, Single p3y, Single p4x, Single p4y,
            BitmapColor color)
        {
            Single minY = Math.Min(Math.Min(p1y, p2y), Math.Min(p3y, p4y));
            Single maxY = Math.Max(Math.Max(p1y, p2y), Math.Max(p3y, p4y));
            for (Single y = minY; y <= maxY; y += 1f)
            {
                Single? l = null, rEdge = null;
                AddEdgeIntersect(p1x, p1y, p2x, p2y, y, ref l, ref rEdge);
                AddEdgeIntersect(p2x, p2y, p3x, p3y, y, ref l, ref rEdge);
                AddEdgeIntersect(p3x, p3y, p4x, p4y, y, ref l, ref rEdge);
                AddEdgeIntersect(p4x, p4y, p1x, p1y, y, ref l, ref rEdge);
                if (l.HasValue && rEdge.HasValue)
                {
                    Single xa = Math.Min(l.Value, rEdge.Value);
                    Single xb = Math.Max(l.Value, rEdge.Value);
                    b.DrawLine(xa, y, xb, y, color, 1f);
                }
            }
        }

        private static void AddEdgeIntersect(Single x1, Single y1, Single x2, Single y2,
            Single y, ref Single? lo, ref Single? hi)
        {
            if (y1 == y2) { return; }
            Single ymin = Math.Min(y1, y2), ymax = Math.Max(y1, y2);
            if (y < ymin || y > ymax) { return; }
            Single t = (y - y1) / (y2 - y1);
            Single x = x1 + t * (x2 - x1);
            if (!lo.HasValue) { lo = x; }
            else if (!hi.HasValue) { hi = x; }
            else
            {
                if (x < lo.Value) { lo = x; }
                else if (x > hi.Value) { hi = x; }
            }
        }

        // Orange/white dots at each of the 7 visible cube vertices.
        public static void IsoCubeVertices(BitmapBuilder b, Single cx, Single cy, Single r,
            BitmapColor dotColor, Single dotRadius)
        {
            Single rx = r * IsoCos;
            Single ry = r * 0.5f;
            (Single x, Single y)[] pts =
            {
                (cx,       cy - r),
                (cx - rx,  cy - ry),
                (cx + rx,  cy - ry),
                (cx,       cy),
                (cx - rx,  cy + ry),
                (cx + rx,  cy + ry),
                (cx,       cy + r),
            };
            foreach (var p in pts)
            {
                b.FillCircle(p.x, p.y, dotRadius, dotColor);
            }
        }

        // ─── Brush (sculpt) ──────────────────────────────────────────────

        public static void Brush(BitmapBuilder b, Single cx, Single cy, Single r,
            BitmapColor body, BitmapColor highlight)
        {
            // Brush tip (large soft disk) + inner highlight + small handle stub
            b.FillCircle(cx, cy + r * 0.15f, r, body);
            b.FillCircle(cx - r * 0.25f, cy - r * 0.05f, r * 0.35f, highlight);
            // Handle pointing up-right
            Single hx1 = cx + r * 0.55f, hy1 = cy - r * 0.45f;
            Single hx2 = cx + r * 0.95f, hy2 = cy - r * 0.90f;
            b.DrawLine(hx1, hy1, hx2, hy2, body, Math.Max(2f, r * 0.18f));
        }

        // ─── Extrude (face + arrow) ──────────────────────────────────────

        public static void ExtrudeArrow(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color, Single thickness)
        {
            // Square face at bottom
            Single half = size * 0.45f;
            Single faceTopY = cy + size * 0.15f;
            Single faceBotY = cy + size * 0.75f;
            Single faceL = cx - half, faceR = cx + half;
            b.DrawLine(faceL, faceTopY, faceR, faceTopY, color, thickness);
            b.DrawLine(faceR, faceTopY, faceR, faceBotY, color, thickness);
            b.DrawLine(faceR, faceBotY, faceL, faceBotY, color, thickness);
            b.DrawLine(faceL, faceBotY, faceL, faceTopY, color, thickness);

            // Arrow shaft up from face
            Single arrowBottom = faceTopY - size * 0.05f;
            Single arrowTop = cy - size * 0.80f;
            b.DrawLine(cx, arrowBottom, cx, arrowTop, color, thickness);

            // Arrowhead
            Single headSize = size * 0.30f;
            b.DrawLine(cx, arrowTop, cx - headSize, arrowTop + headSize, color, thickness);
            b.DrawLine(cx, arrowTop, cx + headSize, arrowTop + headSize, color, thickness);
        }

        // ─── Bevel (square with chamfered corner) ────────────────────────

        public static void BeveledCube(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color, BitmapColor bevelColor, Single thickness)
        {
            Single half = size * 0.5f;
            Single chamfer = size * 0.28f;
            Single l = cx - half, r = cx + half, t = cy - half, btm = cy + half;

            // Square with top-right corner cut
            b.DrawLine(l, t, r - chamfer, t, color, thickness);              // top
            b.DrawLine(r - chamfer, t, r, t + chamfer, bevelColor, thickness); // chamfer edge
            b.DrawLine(r, t + chamfer, r, btm, color, thickness);            // right
            b.DrawLine(r, btm, l, btm, color, thickness);                    // bottom
            b.DrawLine(l, btm, l, t, color, thickness);                      // left

            // Inner parallel edge showing bevel depth
            Single inset = size * 0.14f;
            b.DrawLine(r - chamfer + inset * 0.4f, t + inset * 0.4f,
                       r - inset * 0.4f, t + chamfer - inset * 0.4f,
                       bevelColor, thickness);
        }

        // ─── Loop cut (iso cube + horizontal band) ───────────────────────

        public static void LoopCutCube(BitmapBuilder b, Single cx, Single cy, Single r,
            BitmapColor cubeColor, BitmapColor loopColor, Single thickness)
        {
            IsoCube(b, cx, cy, r, cubeColor, thickness);

            // Horizontal loop at mid-height: two parallel segments across
            // each visible vertical edge, plus rhombus across the front.
            Single rx = r * IsoCos;
            Single ry = r * 0.5f;
            Single midY = cy + ry * 0.5f; // slightly below center
            // Back-plane loop across top of vertical edges
            Single backMidY = cy - ry * 0.5f;
            b.DrawLine(cx - rx, backMidY, cx, cy - 1f, loopColor, thickness);
            b.DrawLine(cx, cy - 1f, cx + rx, backMidY, loopColor, thickness);
            // Front-plane loop
            b.DrawLine(cx - rx, midY, cx, cy + ry, loopColor, thickness);
            b.DrawLine(cx, cy + ry, cx + rx, midY, loopColor, thickness);
            // Sides connecting the two
            b.DrawLine(cx - rx, backMidY, cx - rx, midY, loopColor, thickness);
            b.DrawLine(cx + rx, backMidY, cx + rx, midY, loopColor, thickness);
        }

        // ─── Circular arrow (undo/redo) ──────────────────────────────────

        public static void CurvedArrow(BitmapBuilder b, Int32 cx, Int32 cy, Int32 radius,
            Single startAngle, Single sweepAngle, BitmapColor color, Single thickness)
        {
            b.DrawArc(cx, cy, radius, startAngle, sweepAngle, color, thickness);

            // Arrowhead at the end-angle of the arc. Tangent direction =
            // startAngle + sweepAngle + 90° (perpendicular to radius).
            Single endAngle = (startAngle + sweepAngle) * (Single)(Math.PI / 180.0);
            Single ex = cx + radius * (Single)Math.Cos(endAngle);
            Single ey = cy + radius * (Single)Math.Sin(endAngle);

            // Tangent direction sign follows the sweep sign
            Single tangentSign = sweepAngle >= 0 ? 1f : -1f;
            Single tangent = endAngle + tangentSign * (Single)(Math.PI / 2.0);
            Single headLen = radius * 0.45f;

            // Two lines forming a V at the arc tip
            Single a1 = tangent + (Single)(Math.PI * 0.85); // slight inward
            Single a2 = tangent - (Single)(Math.PI * 0.85);
            b.DrawLine(ex, ey, ex + headLen * (Single)Math.Cos(a1),
                                ey + headLen * (Single)Math.Sin(a1), color, thickness);
            b.DrawLine(ex, ey, ex + headLen * (Single)Math.Cos(a2),
                                ey + headLen * (Single)Math.Sin(a2), color, thickness);
        }

        // ─── Sparkle (4-point star) ──────────────────────────────────────

        public static void Sparkle(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color)
        {
            Single thick = Math.Max(1.5f, size * 0.18f);
            b.DrawLine(cx, cy - size, cx, cy + size, color, thick);
            b.DrawLine(cx - size, cy, cx + size, cy, color, thick);
            // tiny diagonal cross for sparkle shine
            Single s2 = size * 0.45f;
            b.DrawLine(cx - s2, cy - s2, cx + s2, cy + s2, color, thick * 0.6f);
            b.DrawLine(cx - s2, cy + s2, cx + s2, cy - s2, color, thick * 0.6f);
        }

        // ─── Magnifier (zoom) ────────────────────────────────────────────

        public static void Magnifier(BitmapBuilder b, Single cx, Single cy, Single lensRadius,
            String sign, BitmapColor color, Single thickness)
        {
            b.DrawCircle(cx, cy, lensRadius, color);
            if (!String.IsNullOrEmpty(sign))
            {
                Single barLen = lensRadius * 0.55f;
                b.DrawLine(cx - barLen, cy, cx + barLen, cy, color, thickness);
                if (sign == "+")
                {
                    b.DrawLine(cx, cy - barLen, cx, cy + barLen, color, thickness);
                }
            }
            // Handle
            Single hx1 = cx + lensRadius * 0.7f, hy1 = cy + lensRadius * 0.7f;
            Single hx2 = cx + lensRadius * 1.45f, hy2 = cy + lensRadius * 1.45f;
            b.DrawLine(hx1, hy1, hx2, hy2, color, thickness * 1.5f);
        }

        // ─── Floppy disk (save) ──────────────────────────────────────────

        public static void FloppyDisk(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color, Single thickness)
        {
            Single half = size * 0.5f;
            Single l = cx - half, r = cx + half, t = cy - half, btm = cy + half;
            // Outer body with clipped top-right corner (disk sleeve)
            Single notch = size * 0.18f;
            b.DrawLine(l, t, r - notch, t, color, thickness);
            b.DrawLine(r - notch, t, r, t + notch, color, thickness);
            b.DrawLine(r, t + notch, r, btm, color, thickness);
            b.DrawLine(r, btm, l, btm, color, thickness);
            b.DrawLine(l, btm, l, t, color, thickness);
            // Top metal shutter (rectangle in upper ~35%)
            Single shutTop = t + size * 0.12f;
            Single shutBot = t + size * 0.45f;
            Single shutL = l + size * 0.18f;
            Single shutR = r - size * 0.22f;
            b.DrawRectangle((Int32)shutL, (Int32)shutTop,
                (Int32)(shutR - shutL), (Int32)(shutBot - shutTop), color);
            // Label area (rectangle in lower half)
            Single lblTop = t + size * 0.56f;
            Single lblBot = btm - size * 0.10f;
            Single lblL = l + size * 0.16f;
            Single lblR = r - size * 0.16f;
            b.DrawRectangle((Int32)lblL, (Int32)lblTop,
                (Int32)(lblR - lblL), (Int32)(lblBot - lblTop), color);
        }

        // ─── Camera (render) ─────────────────────────────────────────────

        public static void Camera(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color, Single thickness)
        {
            Single w = size, h = size * 0.72f;
            Single l = cx - w / 2f, r = cx + w / 2f;
            Single t = cy - h / 2f, btm = cy + h / 2f;
            // Top bump (viewfinder/eye)
            Single bumpW = w * 0.28f;
            Single bumpL = cx - bumpW * 0.35f, bumpR = cx + bumpW * 0.65f;
            Single bumpT = t - size * 0.12f;
            b.DrawLine(bumpL, bumpT, bumpR, bumpT, color, thickness);
            b.DrawLine(bumpL, bumpT, bumpL, t, color, thickness);
            b.DrawLine(bumpR, bumpT, bumpR, t, color, thickness);
            // Body outline (with gap at bumpL..bumpR on top)
            b.DrawLine(l, t, bumpL, t, color, thickness);
            b.DrawLine(bumpR, t, r, t, color, thickness);
            b.DrawLine(r, t, r, btm, color, thickness);
            b.DrawLine(r, btm, l, btm, color, thickness);
            b.DrawLine(l, btm, l, t, color, thickness);
            // Lens
            Single lensR = Math.Min(w, h) * 0.28f;
            b.DrawCircle(cx, cy + h * 0.05f, lensR, color);
            b.FillCircle(cx, cy + h * 0.05f, lensR * 0.45f, color);
        }

        // ─── Shader sphere (shading toggle) ──────────────────────────────

        public static void ShaderSphere(BitmapBuilder b, Single cx, Single cy, Single r,
            BitmapColor outline, BitmapColor shadedFill, Single thickness)
        {
            b.DrawCircle(cx, cy, r, outline);
            // Fill right half with diagonal stripes to suggest "shaded"
            Int32 minY = (Int32)(cy - r), maxY = (Int32)(cy + r);
            for (Int32 y = minY; y <= maxY; y++)
            {
                Single dy = y - cy;
                Single halfChord = (Single)Math.Sqrt(Math.Max(0f, r * r - dy * dy));
                Single startX = cx; // right half only
                Single endX = cx + halfChord;
                if (endX > startX)
                {
                    // Stripe every 3 rows
                    if (y % 3 == 0)
                    {
                        b.DrawLine(startX, y, endX, y, shadedFill, 1f);
                    }
                }
            }
            // Small specular highlight on left side
            b.FillCircle(cx - r * 0.4f, cy - r * 0.35f, r * 0.12f, outline);
        }

        // ─── Filled 4-point star (favorites) ─────────────────────────────

        public static void Star4(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color, Single thickness)
        {
            // Two overlapping diamonds make a 4-point star look
            Single s = size, s2 = size * 0.35f;
            // Vertical diamond
            b.DrawLine(cx, cy - s, cx - s2, cy, color, thickness);
            b.DrawLine(cx, cy - s, cx + s2, cy, color, thickness);
            b.DrawLine(cx, cy + s, cx - s2, cy, color, thickness);
            b.DrawLine(cx, cy + s, cx + s2, cy, color, thickness);
            // Horizontal diamond
            b.DrawLine(cx - s, cy, cx, cy - s2, color, thickness);
            b.DrawLine(cx - s, cy, cx, cy + s2, color, thickness);
            b.DrawLine(cx + s, cy, cx, cy - s2, color, thickness);
            b.DrawLine(cx + s, cy, cx, cy + s2, color, thickness);
            b.FillCircle(cx, cy, size * 0.18f, color);
        }

        // ─── Strength meter (3 bars) ─────────────────────────────────────

        public static void StrengthBars(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor color)
        {
            Single barW = size * 0.30f;
            Single gap = size * 0.16f;
            Single baseY = cy + size * 0.5f;
            for (Int32 i = 0; i < 3; i++)
            {
                Single h = size * (0.25f + 0.28f * i);
                Single x = cx - barW / 2f;
                Single y = baseY - h;
                b.FillRectangle((Int32)x, (Int32)y, (Int32)barW, (Int32)h, color);
                cx += barW + gap;
            }
        }

        // ─── Filmstrip (frame scrub) ─────────────────────────────────────

        public static void Filmstrip(BitmapBuilder b, Single cx, Single cy, Single w, Single h,
            BitmapColor color, Single thickness)
        {
            Single l = cx - w / 2f, r = cx + w / 2f;
            Single t = cy - h / 2f, btm = cy + h / 2f;
            // Outer strip
            b.DrawRectangle((Int32)l, (Int32)t, (Int32)w, (Int32)h, color);
            // Sprocket holes — two rows
            Single holeH = h * 0.14f;
            Single holeW = w / 10f;
            for (Int32 i = 0; i < 4; i++)
            {
                Single hx = l + w * (0.12f + i * 0.26f);
                b.FillRectangle((Int32)hx, (Int32)(t + h * 0.08f), (Int32)holeW, (Int32)holeH, color);
                b.FillRectangle((Int32)hx, (Int32)(btm - h * 0.08f - holeH), (Int32)holeW, (Int32)holeH, color);
            }
            // Frame dividers
            for (Int32 i = 1; i < 3; i++)
            {
                Single x = l + w * (i / 3f);
                b.DrawLine(x, t + h * 0.28f, x, btm - h * 0.28f, color, thickness);
            }
            // Playhead triangle above the strip
            Single phCx = cx;
            Single phTop = t - h * 0.30f;
            Single phBot = t - thickness;
            Single phHalf = h * 0.20f;
            b.DrawLine(phCx - phHalf, phTop, phCx + phHalf, phTop, color, thickness);
            b.DrawLine(phCx - phHalf, phTop, phCx, phBot, color, thickness);
            b.DrawLine(phCx + phHalf, phTop, phCx, phBot, color, thickness);
        }

        // ─── 3-axis gizmo (view pie) ─────────────────────────────────────

        public static void AxisGizmo(BitmapBuilder b, Single cx, Single cy, Single size,
            BitmapColor xColor, BitmapColor yColor, BitmapColor zColor, Single thickness)
        {
            // Classic Blender-style 3-axis indicator:
            // X → red line right, Y → green line up-right, Z → blue line up
            Single len = size;
            b.DrawLine(cx, cy, cx + len * IsoCos, cy + len * 0.5f, xColor, thickness);
            b.DrawLine(cx, cy, cx - len * IsoCos, cy + len * 0.5f, yColor, thickness);
            b.DrawLine(cx, cy, cx, cy - len, zColor, thickness);
            // Axis labels via small circles at tips
            Single dotR = Math.Max(2.5f, size * 0.10f);
            b.FillCircle(cx + len * IsoCos, cy + len * 0.5f, dotR, xColor);
            b.FillCircle(cx - len * IsoCos, cy + len * 0.5f, dotR, yColor);
            b.FillCircle(cx, cy - len, dotR, zColor);
        }

        // ─── Concentric rings (brush size) ───────────────────────────────

        public static void BrushRings(BitmapBuilder b, Single cx, Single cy, Single maxR,
            BitmapColor color)
        {
            b.DrawCircle(cx, cy, maxR, color);
            b.DrawCircle(cx, cy, maxR * 0.66f, color);
            b.DrawCircle(cx, cy, maxR * 0.33f, color);
            b.FillCircle(cx, cy, Math.Max(1.5f, maxR * 0.12f), color);
        }

        // ─── Circular progress ring ──────────────────────────────────────

        public static void ProgressRing(BitmapBuilder b, Int32 cx, Int32 cy, Int32 radius,
            Single percent, BitmapColor trackColor, BitmapColor fillColor, Single thickness)
        {
            b.DrawCircle(cx, cy, radius, trackColor);
            if (percent > 0)
            {
                Single sweep = Math.Min(360f, 3.6f * percent);
                b.DrawArc(cx, cy, radius, -90f, sweep, fillColor, thickness);
            }
        }
    }
}
