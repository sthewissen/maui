#if MACCATALYST
using System;
using System.Diagnostics.CodeAnalysis;
using CoreGraphics;
using UIKit;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platform;

internal class WindowViewController : UIViewController
{
	WeakReference<IView?> _iTitleBarRef;
	bool _isTitleBarVisible = true;

   	[UnconditionalSuppressMessage("Memory", "MEM0002", Justification = "Proven safe in device test: 'TitleBar Does Not Leak'")]
	UIView? _titleBar;

	[UnconditionalSuppressMessage("Memory", "MEM0002", Justification = "Proven safe in device test: 'TitleBar Does Not Leak'")]
	UIView? _contentWrapperView;

	[UnconditionalSuppressMessage("Memory", "MEM0002", Justification = "Proven safe in device test: 'TitleBar Does Not Leak'")]
	internal NSLayoutConstraint? _contentWrapperTopConstraint;

	/// <summary>
	/// Instantiate a new <see cref="WindowViewController"/> object.
	/// </summary>
	/// <param name="contentViewController">An instance of the <see cref="UIViewController"/> that is the RootViewController.</param>
	/// <param name="window">An instance of the <see cref="IWindow"/>.</param>
	/// <param name="mauiContext">An instance of the <see cref="IMauiContext"/>.</param>
	/// <remarks>
	/// Only dragging the top of the titlebar will move the window.
	/// The top of the TitleBar will also drag the window inside of elements like buttons.
	/// Gestures such as swiping and controls like swipeview will not work inside the TitleBar.
	/// </remarks>
	public WindowViewController(UIViewController contentViewController, IWindow window, IMauiContext mauiContext)
	{
		_iTitleBarRef = new WeakReference<IView?>(null);

		// Note: Maintain the order for adding a new ViewController to a Container ViewController
		// 1. Add the Subview
		// 2. Arrange the Subview's frame
		// 3. AddChildViewController
		// 4. Call DidMoveToParentViewController
		if (View is not null && contentViewController.View is not null)
		{
			_contentWrapperView = new UIView
			{
				TranslatesAutoresizingMaskIntoConstraints = false
			};
			View.AddSubview(_contentWrapperView);
			_contentWrapperView.AddSubview(contentViewController.View);
			_contentWrapperTopConstraint = _contentWrapperView.TopAnchor.ConstraintEqualTo(View.TopAnchor, 0);

			NSLayoutConstraint.ActivateConstraints(new[]
			{
				_contentWrapperView.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
				_contentWrapperView.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
				_contentWrapperTopConstraint,
				_contentWrapperView.BottomAnchor.ConstraintEqualTo(View.BottomAnchor)
			});

		}

		SetUpTitleBar(window, mauiContext, true);
		AddChildViewController(contentViewController);
		contentViewController.DidMoveToParentViewController(this);
	}

	public override void ViewWillLayoutSubviews()
	{
		// Console.WriteLine("WVC ViewWillLayoutSubviews start");

		LayoutTitleBar();

		base.ViewWillLayoutSubviews();

		// Console.WriteLine("WVC ViewWillLayoutSubviews end");
	}

	public override void ViewDidLayoutSubviews()
	{
		// Console.WriteLine("WVC ViewDidLayoutSubviews start");
		UpdateContentWrapperContentFrame();

		base.ViewDidLayoutSubviews();

		// ApplyNavBarHack();
		// Console.WriteLine("WVC ViewDidLayoutSubviews end");
	}

	void UpdateContentWrapperContentFrame()
	{
		// At this point the _contentWrapperView bounds haven't been set
		// so we just use the windows bounds to set this value
		var frame = new CGRect(0, 0, View!.Bounds.Width, View!.Bounds.Height - (_contentWrapperTopConstraint?.Constant ?? 0));

		if (_contentWrapperView is not null && _contentWrapperView.Subviews[0].Frame != frame)
		{
			_contentWrapperView.Subviews[0].Frame = frame;
			Console.WriteLine($"ContentWrapperContentFrame: {frame}");
		}
	}
	
	/// <summary>
	/// Sets up the TitleBar in the ViewController.
	/// </summary>
	/// <param name="window">An instance of the <see cref="IWindow"/>.</param>
	/// <param name="mauiContext">An instance of the <see cref="IMauiContext"/>.</param>
	/// <param name="isInitializing"></param>
	public void SetUpTitleBar(IWindow window, IMauiContext mauiContext, bool isInitializing)
	{
		var platformWindow = window.Handler?.PlatformView as UIWindow;

		if (platformWindow is null || View is null)
		{
			return;
		}

		var newTitleBar = window.TitleBar?.ToPlatform(mauiContext);

		IView? iTitleBar = null;
		_iTitleBarRef?.TryGetTarget(out iTitleBar);

		if (newTitleBar != iTitleBar)
		{
			_titleBar?.RemoveFromSuperview();
			iTitleBar?.DisconnectHandlers();
			iTitleBar = null;

			if (newTitleBar is not null)
			{
				iTitleBar = window.TitleBar;
				View.AddSubview(newTitleBar);
			}

			_titleBar = newTitleBar;
			_iTitleBarRef = new WeakReference<IView?>(iTitleBar);
		}
		
		_isTitleBarVisible = (iTitleBar?.Visibility == Visibility.Visible);

		var platformTitleBar = platformWindow.WindowScene?.Titlebar;

		if (newTitleBar is not null && platformTitleBar is not null)
		{
			platformTitleBar.Toolbar = null;
			platformTitleBar.TitleVisibility = UITitlebarTitleVisibility.Hidden;
		}

		LayoutTitleBar();
	}

	/// <summary>
	/// Measures and arranges the TitleBar and adjusts the frame for the window content to make space for the TitleBar.
	/// </summary>
	public void LayoutTitleBar()
	{
		if (_contentWrapperTopConstraint is null || View is null)
			return;

		var current = _contentWrapperTopConstraint.Constant;
		_iTitleBarRef.TryGetTarget(out var iTitleBar);

		nfloat titleBarHeight = 0;

		if (_isTitleBarVisible && iTitleBar is not null)
		{
			var measured = iTitleBar.Measure(View.Bounds.Width, double.PositiveInfinity);
			iTitleBar.Arrange(new Graphics.Rect(0, 0, View.Bounds.Width, measured.Height));
			titleBarHeight = (nfloat)measured.Height;
		}

		_contentWrapperTopConstraint.Constant = titleBarHeight;
		Console.WriteLine($"New Top Constant: {_contentWrapperTopConstraint.Constant}");

		// if (titleBarHeight <= View.SafeAreaInsets.Top && current > 0 && titleBarHeight != current)
		// {
		// 	// We only care about doing this when we are transitioning from a titlebar with height to one without height
		// 	ApplyNavBarHack();

		// }

		UpdateContentWrapperContentFrame();
	}

	void ApplyNavBarHack()
	{
		if (View is null) return;

		FindAndStopAtNavigationController(this);
		void FindAndStopAtNavigationController(UIViewController viewController)
		{
			foreach (var child in viewController.ChildViewControllers)
			{
				if (child is UINavigationController { NavigationBar: UINavigationBar nb })
				{
					if (_contentWrapperTopConstraint is null || View is null)
						return;

					var frame = nb.Frame;
					if (nb.SafeAreaInsets.Top > 0 && nb.Frame.Y == 0 && _contentWrapperTopConstraint.Constant == 0)
					{
						nb.Frame = new CGRect(frame.X, 36, frame.Width, frame.Height);
					}
					else if(nb.Frame.Y > 0 && _contentWrapperTopConstraint.Constant > 0)
					{
						nb.Frame = new CGRect(frame.X, 0, frame.Width, frame.Height);
					}
				}
				else
				{
					FindAndStopAtNavigationController(child);
				}
			}
		}
	}

	public void SetTitleBarVisibility(bool isVisible)
	{
		if (_contentWrapperTopConstraint is null || View is null)
			return;

		_isTitleBarVisible = isVisible;			
		LayoutTitleBar();
	}
}
#endif // MACCATALYST