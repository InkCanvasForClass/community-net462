using Microsoft.Office.Core;
using Microsoft.Office.Interop.PowerPoint;
using System;
using System.Collections.Generic;

namespace InkCanvasLinkage.Core.Sources
{
    internal static class ComPresentationCapabilities
    {
        public static PresentationMetadata ReadDetails(object presentation, PresentationMetadata metadata)
        {
            bool hidden = false, timing = false;
            VisitTransitions(presentation, transition =>
            {
                hidden |= (int)transition.Hidden == (int)MsoTriState.msoTrue;
                timing |= (int)transition.AdvanceOnTime == (int)MsoTriState.msoTrue && transition.AdvanceTime > 0;
            });
            return new PresentationMetadata(metadata.Name, metadata.FullName, metadata.TotalSlides, hidden, timing);
        }

        private static void VisitTransitions(object presentation, Action<dynamic> visit)
        {
            object slides = null;
            try
            {
                slides = ((dynamic)presentation).Slides;
                int count = ((dynamic)slides).Count;
                for (int i = 1; i <= count; i++)
                {
                    object slide = null, transition = null;
                    try
                    {
                        slide = ((dynamic)slides)[i];
                        transition = ((dynamic)slide).SlideShowTransition;
                        visit(transition);
                    }
                    finally
                    {
                        PresentationLinkSnapshot.Release(transition);
                        PresentationLinkSnapshot.Release(slide);
                    }
                }
            }
            finally { PresentationLinkSnapshot.Release(slides); }
        }

        public static bool Unhide(object presentation)
        {
            VisitTransitions(presentation, transition => transition.Hidden = MsoTriState.msoFalse);
            return true;
        }

        public static bool DisableTimings(object presentation)
        {
            object settings = null;
            try
            {
                settings = ((dynamic)presentation).SlideShowSettings;
                ((dynamic)settings).AdvanceMode = PpSlideShowAdvanceMode.ppSlideShowManualAdvance;
                return true;
            }
            finally { PresentationLinkSnapshot.Release(settings); }
        }

        public static PresentationSmartRegions ReadSmartRegions(object application)
        {
            object windows = null, window = null, presentation = null, setup = null;
            object view = null, slide = null, shapes = null;
            try
            {
                windows = ((dynamic)application).SlideShowWindows;
                if (((dynamic)windows).Count == 0) return new PresentationSmartRegions();
                window = ((dynamic)windows)[1];
                presentation = ((dynamic)window).Presentation;
                setup = ((dynamic)presentation).PageSetup;
                view = ((dynamic)window).View;
                slide = ((dynamic)view).Slide;
                int index = ((dynamic)view).CurrentShowPosition;
                float width = ((dynamic)setup).SlideWidth, height = ((dynamic)setup).SlideHeight;
                IntPtr hwnd = new IntPtr((long)((dynamic)window).HWND);
                shapes = ((dynamic)slide).Shapes;
                var regions = new List<PresentationSmartRegion>();
                int count = ((dynamic)shapes).Count;
                for (int i = 1; i <= count; i++)
                {
                    object shape = null;
                    try
                    {
                        shape = ((dynamic)shapes)[i];
                        dynamic item = shape;
                        if (IsMediaShape(item))
                            regions.Add(new PresentationSmartRegion(item.Left, item.Top, item.Width, item.Height));
                    }
                    finally { PresentationLinkSnapshot.Release(shape); }
                }
                return new PresentationSmartRegions(index, width, height, hwnd, regions);
            }
            finally
            {
                foreach (object item in new[] { shapes, slide, view, setup, presentation, window, windows })
                    PresentationLinkSnapshot.Release(item);
            }
        }

        private static bool IsMediaShape(dynamic shape)
        {
            int type = (int)shape.Type;
            if (type == (int)MsoShapeType.msoMedia || type == (int)MsoShapeType.msoOLEControlObject) return true;
            try { return type == (int)MsoShapeType.msoEmbeddedOLEObject && (int)shape.MediaType == 14; }
            catch { return false; }
        }
    }
}
