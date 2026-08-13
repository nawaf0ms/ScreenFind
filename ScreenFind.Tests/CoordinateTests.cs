using ScreenFind.Core.Extraction;
using ScreenFind.Core.Models;
using Xunit;

namespace ScreenFind.Tests;

/// <summary>
/// Spec §5.3: "write a unit test for this function specifically — coordinate errors are the
/// biggest source of bugs in this kind of app".
/// </summary>
public class CoordinateTests
{
    [Fact]
    public void UndoesTheTwoTimesUpscale()
    {
        var geometry = new CaptureGeometry(new Rect(0, 0, 800, 600), 2.0);

        var screen = CoordinateMapper.ImageToScreen(new Rect(100, 50, 40, 20), geometry);

        Assert.Equal(new Rect(50, 25, 20, 10), screen);
    }

    [Fact]
    public void AddsCaptureOrigin()
    {
        var geometry = new CaptureGeometry(new Rect(960, 540, 800, 600), 2.0);

        var screen = CoordinateMapper.ImageToScreen(new Rect(100, 50, 40, 20), geometry);

        Assert.Equal(new Rect(1010, 565, 20, 10), screen);
    }

    [Fact]
    public void ScaleOfOneIsAPureOffset()
    {
        var geometry = new CaptureGeometry(new Rect(10, 20, 100, 100), 1.0);

        var screen = CoordinateMapper.ImageToScreen(new Rect(5, 5, 30, 10), geometry);

        Assert.Equal(new Rect(15, 25, 30, 10), screen);
    }

    [Fact]
    public void InvalidScaleFallsBackToOne()
    {
        var geometry = new CaptureGeometry(new Rect(0, 0, 100, 100), 0);

        var screen = CoordinateMapper.ImageToScreen(new Rect(5, 5, 10, 10), geometry);

        Assert.Equal(new Rect(5, 5, 10, 10), screen);
    }

    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2.0)]
    public void DpiScaleIsDerivedFrom96(double dpi, double expected)
        => Assert.Equal(expected, CoordinateMapper.DpiScale(dpi));

    [Fact]
    public void ScreenToDipDividesByScale()
    {
        var dip = CoordinateMapper.ScreenToDip(new Rect(300, 150, 60, 30), 1.5);
        Assert.Equal(new Rect(200, 100, 40, 20), dip);
    }

    [Fact]
    public void DipAndScreenRoundTrip()
    {
        var original = new Rect(1234, 567, 89, 21);

        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var back = CoordinateMapper.DipToScreen(CoordinateMapper.ScreenToDip(original, scale), scale);
            Assert.Equal(original.X, back.X, 6);
            Assert.Equal(original.Y, back.Y, 6);
            Assert.Equal(original.Width, back.Width, 6);
            Assert.Equal(original.Height, back.Height, 6);
        }
    }

    [Fact]
    public void SecondMonitorCoordinatesAreMadeLocal()
    {
        var monitor = new Rect(1920, 0, 2560, 1440);

        var local = CoordinateMapper.ScreenToMonitorLocal(new Rect(2020, 100, 50, 20), monitor);

        Assert.Equal(new Rect(100, 100, 50, 20), local);
    }

    /// <summary>
    /// Spec §7 phase 2 acceptance: on a 150% display, a highlight must land within 3 physical
    /// pixels of the word. This walks the full chain: OCR box -> screen -> overlay DIPs -> screen.
    /// </summary>
    [Fact]
    public void FullChainStaysWithinThreePixelsOnA150PercentDisplay()
    {
        const double dpiScale = 1.5;
        var window = new Rect(300, 200, 1200, 900);          // physical pixels
        var geometry = new CaptureGeometry(window, 2.0);
        var expectedScreen = new Rect(400, 250, 40, 20);      // where the word actually is

        // What OCR reports for that word in the ×2 upscaled capture.
        var imageRect = new Rect(
            (expectedScreen.X - window.X) * 2,
            (expectedScreen.Y - window.Y) * 2,
            expectedScreen.Width * 2,
            expectedScreen.Height * 2);

        var screen = CoordinateMapper.ImageToScreen(imageRect, geometry);
        var monitorLocal = CoordinateMapper.ScreenToMonitorLocal(screen, new Rect(0, 0, 2560, 1440));
        var dip = CoordinateMapper.ScreenToDip(monitorLocal, dpiScale);
        var rendered = CoordinateMapper.DipToScreen(dip, dpiScale);

        Assert.True(Math.Abs(rendered.X - expectedScreen.X) < 3, $"x drift {rendered.X - expectedScreen.X}");
        Assert.True(Math.Abs(rendered.Y - expectedScreen.Y) < 3, $"y drift {rendered.Y - expectedScreen.Y}");
        Assert.True(Math.Abs(rendered.Width - expectedScreen.Width) < 3, "width drift");
        Assert.True(Math.Abs(rendered.Height - expectedScreen.Height) < 3, "height drift");
    }
}
