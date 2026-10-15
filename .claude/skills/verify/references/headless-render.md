# Rendering a XAML layout on Linux

Verify proves a XAML change compiles; it shows nothing about how it looks. For a layout change, a
throwaway Avalonia.Headless project renders the real markup with the app's real styles to PNG — no
Windows, no app start. First used 2026-10-15 for the two-row title strip with a customer logo; it
caught a squeezed stats label and a test image with a transparent margin before the user ever
installed a build.

Build it in the scratchpad, never in the repo.

## Project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <RollForward>LatestMajor</RollForward>
  </PropertyGroup>
  <ItemGroup>
    <!-- Same version as the app's Avalonia, or the XAML compiler and runtime disagree. -->
    <PackageReference Include="Avalonia" Version="12.0.3" />
    <PackageReference Include="Avalonia.Headless" Version="12.0.3" />
    <PackageReference Include="Avalonia.Skia" Version="12.0.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.3" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.3" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <!-- For the app's styles: avares://Entra-PIM-Manager/Styles/*.axaml -->
    <ProjectReference Include="<repo>/src/Entra-PIM-Manager.App.Avalonia/Entra-PIM-Manager.App.Avalonia.csproj" />
  </ItemGroup>
</Project>
```

Build with `dotnet build -c Release -p:EnableWindowsTargeting=true`. The property must come from the
command line: set only in the csproj, it does not reach the referenced App and Core projects, and
they fail with `NETSDK1100`.

## The pieces

- **`App.axaml`** — copy the `Application.Resources` and `Application.Styles` blocks of the app's
  `App.axaml` (Theme.axaml, FluentTheme, Buttons.axaml, Cards.axaml). Leave out the tray icon.
- **A window** whose content is the block under test, copied **verbatim** out of
  `TrayPopupWindow.axaml` by a script (start and end marker comments), so the render shows the
  committed markup rather than a retyped one. Wrap it the way the popup does: `Width="380"`, a
  `Border Margin="4"`, a `StackPanel Margin="12,10,16,10"` (16 = 12 plus the scroll bar padding).
- **Stub view models** with exactly the property and command names the block binds to, as the
  window's `x:DataType`. Compiled bindings then check the names at build time, as in the app.
- **`Main`**:

  ```csharp
  AppBuilder.Configure<App>()
      .UseSkia()
      .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
      .WithInterFont()
      .SetupWithoutStarting();

  Application.Current!.RequestedThemeVariant = ThemeVariant.Light;   // and Dark
  var w = new StripWindow { DataContext = vm };
  w.Show();
  Dispatcher.UIThread.RunJobs();
  AvaloniaHeadlessPlatform.ForceRenderTimerTick();
  using var frame = w.CaptureRenderedFrame()!;
  frame.Save("out.png");
  ```

  `UseHeadlessDrawing = false` plus `UseSkia()` is what produces real pixels; the default headless
  drawing renders nothing.

## Traps

- **Test images must be tightly cropped.** A generated test PNG with transparent padding renders
  smaller and off the edge, and reads as a layout bug. Size the canvas to the measured content.
- Render light **and** dark, and the state without the optional element, since "nothing changes
  without X" is usually part of the requirement.
- `montage -label '%t' *.png -tile 2x -geometry +12+12 sheet.png` (ImageMagick) puts the variants
  on one sheet for the user.
