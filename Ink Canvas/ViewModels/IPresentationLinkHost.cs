using InkCanvasLinkage.Core;
using System.Threading.Tasks;

namespace Ink_Canvas.ViewModels
{
    /// <summary>Canvas and window effects; never owns or operates a presentation source.</summary>
    public interface IPresentationLinkHost
    {
        void BeforeNavigate(PresentationLinkState state);
        void NavigationCompleted(bool succeeded, bool skipAnimations);
        Task BeforeEndSlideShowAsync();
        void PrepareSlideNavigation();
        Task SlideNavigationCompletedAsync(bool activate);
        Task<int> ReadPlaybackPositionAsync(PresentationMetadata presentation);
        Task<bool> ConfirmAsync(string content);
        void ConnectionCheckCompleted(bool isConnected);
    }
}
