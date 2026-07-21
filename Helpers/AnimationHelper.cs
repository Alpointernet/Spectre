using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace Spectre.Helpers
{
    public static class AnimationHelper
    {
        public static readonly DependencyProperty FadeInOnVisibleProperty =
            DependencyProperty.RegisterAttached("FadeInOnVisible", typeof(bool), typeof(AnimationHelper), new PropertyMetadata(false, OnFadeInOnVisibleChanged));

        public static void SetFadeInOnVisible(UIElement element, bool value) => element.SetValue(FadeInOnVisibleProperty, value);
        public static bool GetFadeInOnVisible(UIElement element) => (bool)element.GetValue(FadeInOnVisibleProperty);

        private static void OnFadeInOnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element && (bool)e.NewValue)
            {
                // Trigger on IsVisibleChanged for controls that use Visibility (e.g. Auto)
                element.IsVisibleChanged += (s, args) =>
                {
                    if ((bool)args.NewValue)
                        DoFadeIn(element);
                    else
                        Hide(element);
                };

                // For ScrollBars with Visibility=Visible but inactive, trigger on Maximum > 0
                if (element is ScrollBar scrollBar)
                {
                    var dpd = DependencyPropertyDescriptor.FromProperty(RangeBase.MaximumProperty, typeof(ScrollBar));
                    dpd?.AddValueChanged(scrollBar, (s, args) =>
                    {
                        if (scrollBar.IsVisible)
                        {
                            if (scrollBar.Maximum > 0)
                                DoFadeIn(scrollBar);
                            else
                                Hide(scrollBar);
                        }
                    });
                }

                if (element.IsVisible)
                {
                    if (element is ScrollBar sb && sb.Maximum <= 0)
                        Hide(element, true);
                    else
                        DoFadeIn(element, true);
                }
                else
                {
                    Hide(element, true);
                }
            }
        }

        private static void DoFadeIn(UIElement element, bool instant = false)
        {
            if (instant)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1.0;
                return;
            }

            var anim = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.4)) 
            { 
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } 
            };
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private static void Hide(UIElement element, bool instant = false)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 0.0;
        }
    }
}
