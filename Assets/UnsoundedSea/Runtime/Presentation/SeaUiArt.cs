using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Original vector engravings keep instrument silhouettes sharp at every panel scale.
    internal sealed class SeaUiArt : VisualElement
    {
        private readonly string motif;
        private static readonly Color Brass = new Color32(185, 151, 89, 255);
        private static readonly Color Light = new Color32(231, 211, 164, 255);
        private static readonly Color Ink = new Color32(9, 23, 25, 255);
        private static readonly Color Patina = new Color32(103, 161, 145, 255);

        public SeaUiArt(string motif)
        {
            this.motif = motif;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float scale = Mathf.Min(contentRect.width, contentRect.height) / 200f;
            if (scale <= 0) return;
            var origin = contentRect.center;
            Vector2 At(float x, float y) => origin + new Vector2(x - 100, y - 100) * scale;
            void Line(float x1, float y1, float x2, float y2)
            {
                p.BeginPath(); p.MoveTo(At(x1, y1)); p.LineTo(At(x2, y2)); p.Stroke();
            }
            void Ring(float x, float y, float radius, bool fill = false)
            {
                p.BeginPath(); p.Arc(At(x, y), radius * scale, 0, 360);
                if (fill) p.Fill(); else p.Stroke();
            }
            void Diamond(float x, float y, float width, float height)
            {
                p.BeginPath(); p.MoveTo(At(x, y - height)); p.LineTo(At(x + width, y));
                p.LineTo(At(x, y + height)); p.LineTo(At(x - width, y)); p.ClosePath(); p.Fill(); p.Stroke();
            }
            p.lineWidth = 1.4f * scale;
            p.strokeColor = Brass;
            p.fillColor = Ink;
            bool instrument = motif == "compass" || motif == "seal" || motif == "heart" || motif == "eye";
            if (instrument)
            {
                Ring(100, 100, 93, true);
                p.lineWidth = 3 * scale; Ring(100, 100, 92);
                p.lineWidth = scale; Ring(100, 100, 87); Ring(100, 100, 76);
                p.strokeColor = new Color(0.44f, 0.39f, 0.25f, 0.7f);
                for (int i = 0; i < 48; i++)
                {
                    float angle = i * Mathf.PI / 24;
                    float inner = i % 4 == 0 ? 78 : 82;
                    Line(100 + Mathf.Cos(angle) * inner, 100 + Mathf.Sin(angle) * inner,
                         100 + Mathf.Cos(angle) * 86, 100 + Mathf.Sin(angle) * 86);
                }
                p.strokeColor = Light;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI / 2;
                    Ring(100 + Mathf.Cos(angle) * 91, 100 + Mathf.Sin(angle) * 91, 2);
                }
            }
            else
            {
                p.strokeColor = new Color(0.56f, 0.48f, 0.3f, 0.35f);
                Ring(100, 100, 68);
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * Mathf.PI / 6;
                    Line(100 + Mathf.Cos(angle) * 73, 100 + Mathf.Sin(angle) * 73,
                         100 + Mathf.Cos(angle) * 80, 100 + Mathf.Sin(angle) * 80);
                }
            }
            p.strokeColor = Brass; p.lineWidth = 1.8f * scale;
            switch (motif)
            {
                case "switch-off":
                case "switch-positive":
                case "switch-reverse":
                    p.lineWidth=4*scale;Ring(100,100,58);Ring(67,124,8);Ring(133,124,8);
                    Line(35,124,59,124);Line(141,124,165,124);
                    if(motif=="switch-off")Line(74,118,119,73);
                    else if(motif=="switch-positive"){Line(75,124,125,124);Line(100,51,100,79);Line(86,65,114,65);}
                    else {Line(75,124,125,124);Line(86,65,114,65);Line(82,92,118,112);Line(118,92,82,112);}
                    break;
                case "compass":
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4 - Mathf.PI / 2;
                        float length = i % 2 == 0 ? 70 : 45;
                        var tip = At(100 + Mathf.Cos(a) * length, 100 + Mathf.Sin(a) * length);
                        var side = At(100 + Mathf.Cos(a + Mathf.PI / 2) * 10, 100 + Mathf.Sin(a + Mathf.PI / 2) * 10);
                        p.fillColor = i == 0 ? Light : Brass;
                        p.BeginPath(); p.MoveTo(At(100, 100)); p.LineTo(tip); p.LineTo(side); p.ClosePath(); p.Fill();
                        p.BeginPath(); p.MoveTo(tip); p.LineTo(At(100 - Mathf.Cos(a + Mathf.PI / 2) * 10, 100 - Mathf.Sin(a + Mathf.PI / 2) * 10)); p.LineTo(At(100, 100)); p.Stroke();
                    }
                    p.fillColor = Ink; Ring(100, 100, 8, true); Ring(100, 100, 8);
                    break;
                case "bearing":
                    p.fillColor = Light;
                    p.BeginPath(); p.MoveTo(At(100, 24)); p.LineTo(At(119, 100)); p.LineTo(At(100, 87));
                    p.LineTo(At(81, 100)); p.ClosePath(); p.Fill(); p.Stroke();
                    Line(100, 87, 100, 170); Ring(100, 176, 4);
                    break;
                case "heart":
                    p.fillColor = new Color32(111, 44, 41, 255);
                    p.BeginPath(); p.MoveTo(At(100, 136)); p.LineTo(At(64, 100)); p.LineTo(At(60, 80));
                    p.LineTo(At(72, 65)); p.LineTo(At(88, 65)); p.LineTo(At(100, 80)); p.LineTo(At(112, 65));
                    p.LineTo(At(128, 65)); p.LineTo(At(140, 80)); p.LineTo(At(136, 100)); p.ClosePath(); p.Fill(); p.Stroke();
                    p.strokeColor = Light; Line(67, 96, 87, 96); Line(87, 96, 94, 83); Line(94, 83, 103, 111); Line(103, 111, 112, 94); Line(112, 94, 134, 94);
                    break;
                case "eye":
                    p.strokeColor = Patina;
                    p.BeginPath();
                    for (int i = 0; i <= 64; i++)
                    {
                        float a = i * Mathf.PI / 32;
                        var point = At(100 + Mathf.Cos(a) * 53, 96 + Mathf.Sin(a) * 27 * Mathf.Abs(Mathf.Sin(a)));
                        if (i == 0) p.MoveTo(point); else p.LineTo(point);
                    }
                    p.ClosePath(); p.Stroke(); Ring(100, 96, 21); Ring(100, 96, 16);
                    p.fillColor = Light; Diamond(100, 96, 4, 14);
                    for (int i = -2; i <= 2; i++) Line(100 + i * 17, 64 - (2 - Mathf.Abs(i)) * 3, 100 + i * 21, 49 - (2 - Mathf.Abs(i)) * 7);
                    Line(100, 126, 100, 144); Ring(100, 149, 3);
                    break;
                case "lantern":
                    Ring(100, 45, 13); Line(87, 50, 80, 67); Line(113, 50, 120, 67);
                    p.fillColor = new Color32(93, 73, 36, 255);
                    p.BeginPath(); p.MoveTo(At(75, 77)); p.LineTo(At(125, 77)); p.LineTo(At(137, 139)); p.LineTo(At(63, 139)); p.ClosePath(); p.Fill(); p.Stroke();
                    Line(70, 70, 130, 70); Line(63, 145, 137, 145); Line(70, 153, 130, 153);
                    Line(83, 79, 78, 136); Line(117, 79, 122, 136);
                    p.fillColor = Light; Diamond(100, 111, 10, 24); Line(100, 135, 100, 140);
                    for (int i = 0; i < 5; i++) { Line(48, 82 + i * 14, 55, 87 + i * 12); Line(152, 82 + i * 14, 145, 87 + i * 12); }
                    break;
                case "gun":
                    p.fillColor = new Color32(48, 60, 56, 255);
                    p.BeginPath(); p.MoveTo(At(44, 77)); p.LineTo(At(153, 77)); p.LineTo(At(156, 91));
                    p.LineTo(At(106, 94)); p.LineTo(At(96, 112)); p.LineTo(At(77, 112)); p.LineTo(At(68, 148));
                    p.LineTo(At(41, 143)); p.LineTo(At(53, 103)); p.LineTo(At(42, 95)); p.ClosePath(); p.Fill(); p.Stroke();
                    Line(105, 83, 150, 83); Ring(81, 91, 13); Ring(81, 91, 5);
                    Line(51, 72, 62, 63); Line(62, 63, 67, 77); Line(141, 77, 141, 71);
                    for (int i = 0; i < 5; i++) Line(50 + i * 2, 135 - i * 6, 67 + i * 2, 140 - i * 6);
                    Ring(100, 105, 11); Line(100, 97, 98, 106);
                    break;
                case "shield":
                    p.fillColor = new Color32(36, 63, 56, 255);
                    p.BeginPath(); p.MoveTo(At(100, 39)); p.LineTo(At(144, 61)); p.LineTo(At(137, 118));
                    p.LineTo(At(122, 142)); p.LineTo(At(100, 161)); p.LineTo(At(78, 142)); p.LineTo(At(63, 118)); p.LineTo(At(56, 61)); p.ClosePath(); p.Fill(); p.Stroke();
                    Line(100, 50, 100, 148); Line(67, 70, 133, 70);
                    p.fillColor = Brass; Diamond(100, 100, 18, 27);
                    p.strokeColor = Light; Line(100, 83, 100, 115); Line(89, 98, 111, 98);
                    break;
                case "ear":
                case "shell":
                    for (int i = 0; i < 9; i++)
                    {
                        float a = Mathf.PI * (0.12f + i * 0.095f);
                        float x = 100 + Mathf.Cos(a) * 56, y = 103 - Mathf.Sin(a) * 58;
                        Line(100, 149, x, y); Ring(x, y, 5);
                    }
                    p.BeginPath(); p.Arc(At(100, 103), 57 * scale, 195, 345); p.Stroke();
                    Line(86, 151, 114, 151); Ring(100, 144, 7);
                    break;
                case "scroll":
                    p.fillColor = new Color32(51, 57, 44, 255);
                    p.BeginPath(); p.MoveTo(At(59, 50)); p.LineTo(At(145, 50)); p.LineTo(At(135, 149)); p.LineTo(At(48, 149)); p.ClosePath(); p.Fill(); p.Stroke();
                    Ring(60, 51, 10); Ring(135, 149, 10); Line(60, 41, 145, 41); Line(48, 159, 135, 159);
                    p.strokeColor = Patina;
                    for (int i = 0; i < 4; i++)
                    {
                        p.BeginPath();
                        for (int j = 0; j <= 20; j++)
                        {
                            var point = At(65 + j * 3, 79 + i * 14 + Mathf.Sin(j * 0.42f + i) * 3);
                            if (j == 0) p.MoveTo(point); else p.LineTo(point);
                        }
                        p.Stroke();
                    }
                    p.strokeColor = Light; Line(85, 118, 111, 81); Ring(85, 118, 4); Ring(111, 81, 4);
                    break;
                case "seal":
                case "tentacle":
                    for (int arm = 0; arm < 5; arm++)
                    {
                        float angle = arm * Mathf.PI * 2 / 5;
                        p.BeginPath();
                        for (int i = 0; i <= 48; i++)
                        {
                            float t = i / 48f;
                            float a = angle + t * 5.7f;
                            float radius = 12 + t * 45;
                            var point = At(100 + Mathf.Cos(a) * radius, 100 + Mathf.Sin(a) * radius);
                            if (i == 0) p.MoveTo(point); else p.LineTo(point);
                        }
                        p.Stroke();
                    }
                    p.fillColor = Patina; Diamond(100, 100, 10, 16); Ring(100, 100, 24);
                    break;
            }
        }
    }
}
