using System;
using System.IO;
using System.Linq;
using System.Windows.Ink;
using System.Windows.Input;
using Ink_Canvas.Helpers;
using InkUInk;
using InkUInk.Model;
using InkUInk.MsgPack;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        RecoversAroundRejectedObjects();
        RoundTripsUndoGroupsWithoutChangingInput();
        ConvertsDpiHighlighterAndViewport();
        DoesNotOverwriteAnExistingCopy();
        Console.WriteLine("UInk self-tests passed.");
    }

    private static void RecoversAroundRejectedObjects()
    {
        var pageA = Page(10);
        pageA.Add(Page(20));
        var bytes = UInkWriter.WriteToBytes(UInkStrokeConverter.Build(new[] { pageA, Page(30) }, null));
        var reader = new MsgPackReader(bytes);
        reader.ReadValue(); // Header
        reader.ReadValue(); // Header Extension
        reader.ReadValue(); // Canvas A
        int inkStart = reader.Position;
        reader.ReadValue(); // first Ink A
        int firstInkEnd = reader.Position;
        reader.ReadValue(); // second Ink A
        int pageEnd = reader.Position;

        foreach (bool repeatedType in new[] { false, true })
        {
            using var stream = new MemoryStream();
            stream.Write(bytes, 0, firstInkEnd);
            var writer = new MsgPackWriter(stream);

            // Reject a malformed Ink without ending A's scope. The field after the nested Map must be consumed.
            WriteNestedDuplicate(writer, "color", "fallback", UInkTypeId.Ink);
            stream.Write(bytes, firstInkEnd, pageEnd - firstInkEnd);

            if (repeatedType)
            {
                writer.WriteMapHeader(2);
                WriteType(writer, UInkTypeId.Ink);
                WriteType(writer, UInkTypeId.Canvas);
            }
            else
            {
                WriteNestedDuplicate(writer, "viewport", "scale", UInkTypeId.Canvas);
            }

            stream.Write(bytes, inkStart, firstInkEnd - inkStart); // Ink B must not leak into A.
            stream.Write(bytes, pageEnd, bytes.Length - pageEnd);

            var recovered = UInkReader.Read(stream.ToArray());
            Check(recovered.Status == UInkReadStatus.Ok && recovered.Pages.Count == 2, "A and C survive invalid B");
            Check(recovered.Pages[0].Layers[0].Blocks.Cast<UInkInk>().Select(ink => ink.Points[0].X).SequenceEqual(new[] { 10.0, 20.0 }),
                "invalid Ink does not close A; B does");
            var c = (UInkInk)recovered.Pages[1].Layers[0].Blocks.Single();
            Check(c.Points[0].X == 30, "C's ink stays on C");
            Check(recovered.Warnings.Count(w => w.Contains("重复出现键")) == 2, "duplicate blocks are reported");
        }
    }

    private static void WriteNestedDuplicate(MsgPackWriter writer, string mapKey, string duplicateKey, ushort type)
    {
        writer.WriteMapHeader(3);
        writer.WriteString(mapKey);
        writer.WriteMapHeader(2);
        writer.WriteString(duplicateKey);
        writer.WriteUInt32(1);
        writer.WriteString(duplicateKey);
        writer.WriteUInt32(1);
        writer.WriteString("contentId");
        writer.WriteUInt32(0);
        WriteType(writer, type); // Identify the rejected block even when its type follows the nested Map.
    }

    private static void WriteType(MsgPackWriter writer, ushort type)
    {
        writer.WriteString("type");
        writer.WriteUInt16(type);
    }

    private static void RoundTripsUndoGroupsWithoutChangingInput()
    {
        var strokes = Page(10);
        strokes.Add(Page(20));
        strokes.Add(Page(30));
        var source = UInkStrokeConverter.Build(new[] { strokes }, null);
        var blocks = source.Pages[0].Layers[0].Blocks;
        blocks[1].ContentId = 12;
        blocks[1].UndoId = 2;
        var ink = (UInkInk)blocks[1];
        ink.Points[0].Width = -1;
        ink.Nib = new UInkNib { Shape = 1, Aspect = 0.5, Orientation = 99 };
        var shape = Line();
        shape.UndoId = 3;
        shape.Stroke.Width = -1;
        shape.Geometry.Points.Add(new UInkGeomPoint { X = 1400, Y = 100 });
        blocks.Add(shape);

        var bytes = UInkWriter.WriteToBytes(source);
        var roundTrip = UInkReader.Read(bytes).Pages[0].Layers[0].Blocks;
        Check(roundTrip.Select(b => b.UndoId).SequenceEqual(new uint[] { 0, 1, 1, 2 }), "grouped ink stays one undo group");
        Check(ink.ContentId == 12 && ink.UndoId == 2 && ink.Points[0].Width == -1 && ink.Nib.Orientation == 99,
            "write does not change source IDs or nested stroke data");
        Check(shape.Stroke.Width == -1 && shape.Geometry.Points.Count == 3, "write does not change source shape style or geometry");
        Check(bytes.SequenceEqual(UInkWriter.WriteToBytes(source)), "writing the same document twice gives the same bytes");
    }

    private static void ConvertsDpiHighlighterAndViewport()
    {
        foreach (double dpi in new[] { 1.0, 1.5 })
        {
            var strokes = Page(300);
            var attributes = strokes[0].DrawingAttributes;
            attributes.StylusTip = StylusTip.Rectangle;
            attributes.Width = 4;
            attributes.Height = 8;
            attributes.IsHighlighter = true;

            var document = UInkStrokeConverter.Build(new[] { strokes }, new UInkBuildOptions { DpiScale = dpi });
            var page = UInkReader.Read(UInkWriter.WriteToBytes(document)).Pages[0];
            var ink = (UInkInk)page.Layers[0].Blocks.Single();
            Check(ink.InkType == UInkInkType.Highlighter, "default 2:1 highlighter stays a highlighter");
            Check(page.PrimaryCanvas.Viewport == null && ink.Points[0].X == 300 * dpi && ink.Points[0].Width == 8 * dpi,
                "export uses device pixels without viewport scaling");
            var imported = UInkStrokeConverter.ToStrokeCollection(page, out _, dpi)[0];
            Check(imported.StylusPoints[0].X == 300 && imported.DrawingAttributes.Height == 8 && imported.DrawingAttributes.IsHighlighter,
                "round-trip restores DIP coordinates, width and highlighter flag");
        }

        var layeredPage = new UInkPage();
        layeredPage.Layers.Add(new UInkLayer { Canvas = new UInkCanvas { Viewport = new UInkViewport { X = 200, Y = 50, Scale = 3 } } });
        var layer = new UInkLayer { Canvas = new UInkCanvas { LayerIndex = 1 } };
        layeredPage.Layers.Add(layer);
        layer.Blocks.Add(UInkStrokeConverter.ToInk(Page(300)[0], 0, 0));
        layer.Blocks.Add(Line());
        var mapped = UInkStrokeConverter.ToStrokeCollection(layeredPage, out _, 1.5);
        Check(mapped.Count == 2 && mapped[1].StylusPoints.Count == 2, "dense dashes fall back to one solid stroke");
        Check(mapped[0].StylusPoints[0].X == 200 && mapped[0].DrawingAttributes.Width == 16, "Ink inherits layer zero viewport");
        Check(mapped[1].StylusPoints[0].X == 200 && mapped[1].StylusPoints[0].Y == 100 && mapped[1].DrawingAttributes.Width == 6,
            "Shape inherits layer zero viewport for position and width");
    }

    private static void DoesNotOverwriteAnExistingCopy()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] original = { 1, 2, 3 };
            File.WriteAllBytes(path, original);
            var document = UInkStrokeConverter.Build(new[] { Page(10) }, null);
            try
            {
                UInkWriter.WriteFile(document, path, new UInkWriteOptions { Overwrite = false });
                throw new InvalidOperationException("writing a copy unexpectedly succeeded");
            }
            catch (IOException)
            {
                Check(original.SequenceEqual(File.ReadAllBytes(path)), "an existing destination is left intact");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static StrokeCollection Page(double x) => new StrokeCollection
    {
        new Stroke(new StylusPointCollection { new StylusPoint(x, 100), new StylusPoint(x + 20, 100) },
            new DrawingAttributes { Width = 8, Height = 8, IgnorePressure = true })
    };

    private static UInkShape Line() => new UInkShape
    {
        ShapeType = UInkShapeType.Line,
        Geometry = new UInkGeometry
        {
            Points = { new UInkGeomPoint { X = 300, Y = 100 }, new UInkGeomPoint { X = 1300, Y = 100 } }
        },
        Stroke = new UInkStrokeStyle { Width = 3, DashArray = new[] { 0.0001, 0.0001 } }
    };

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
