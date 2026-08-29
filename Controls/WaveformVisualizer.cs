using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace JustSTT.Controls
{
    public class WaveformVisualizer : FrameworkElement
    {
        private const int BarCount = 7;
        private readonly double[] _targetHeights = new double[BarCount];
        private readonly double[] _currentHeights = new double[BarCount];
        private readonly DispatcherTimer _animTimer;
        private readonly Random _random = new();

        public static readonly DependencyProperty BarBrushProperty =
            DependencyProperty.Register(
                nameof(BarBrush),
                typeof(Brush),
                typeof(WaveformVisualizer),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush BarBrush
        {
            get => (Brush)GetValue(BarBrushProperty);
            set => SetValue(BarBrushProperty, value);
        }

        public WaveformVisualizer()
        {
            Width = 56;
            Height = 22;

            for (int i = 0; i < BarCount; i++)
            {
                _currentHeights[i] = 4;
                _targetHeights[i] = 4;
            }

            _animTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16) // 60 FPS only during active animation
            };
            _animTimer.Tick += OnAnimTick;
        }

        public void SetLevel(float level)
        {
            double clamped = Math.Clamp(level, 0f, 1f);
            double maxH = Math.Max(Height - 4, 12);

            for (int i = 0; i < BarCount; i++)
            {
                // Shape: center bars are taller, side bars are shorter (bell curve)
                double bell = Math.Sin((i + 1) * Math.PI / (BarCount + 1));
                double jitter = (_random.NextDouble() * 0.35) + 0.8;
                double h = 4 + (clamped * maxH * bell * jitter);
                _targetHeights[i] = Math.Clamp(h, 4, maxH);
            }

            if (!_animTimer.IsEnabled)
            {
                _animTimer.Start();
            }
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            bool needsRedraw = false;
            bool stillActive = false;

            for (int i = 0; i < BarCount; i++)
            {
                double diff = _targetHeights[i] - _currentHeights[i];
                if (Math.Abs(diff) > 0.25)
                {
                    _currentHeights[i] += diff * 0.35; // smooth interpolation
                    needsRedraw = true;
                }
                else
                {
                    _currentHeights[i] = _targetHeights[i];
                }

                // Slowly decay towards idle height (4px)
                _targetHeights[i] = Math.Max(4, _targetHeights[i] * 0.92);

                if (_currentHeights[i] > 4.1 || _targetHeights[i] > 4.1)
                {
                    stillActive = true;
                }
            }

            if (needsRedraw)
            {
                InvalidateVisual();
            }

            // Stop timer when all bars have settled down to eliminate idle CPU cycles
            if (!stillActive && _animTimer.IsEnabled)
            {
                _animTimer.Stop();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double totalWidth = ActualWidth > 0 ? ActualWidth : Width;
            double totalHeight = ActualHeight > 0 ? ActualHeight : Height;

            double barWidth = 3.5;
            double spacing = (totalWidth - (BarCount * barWidth)) / (BarCount - 1);

            double centerY = totalHeight / 2.0;

            var brush = BarBrush ?? Brushes.White;

            for (int i = 0; i < BarCount; i++)
            {
                double h = Math.Max(3, _currentHeights[i]);
                double x = i * (barWidth + spacing);
                double y = centerY - (h / 2.0);

                var rect = new Rect(x, y, barWidth, h);
                dc.DrawRoundedRectangle(brush, null, rect, barWidth / 2.0, barWidth / 2.0);
            }
        }
    }
}
