namespace EntraPimManager.AppAvalonia;

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using EntraPimManager.AppAvalonia.Services;
using EntraPimManager.AppAvalonia.Tray;
using EntraPimManager.AppAvalonia.ViewModels;
using EntraPimManager.AppAvalonia.Views;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Caching;
using EntraPimManager.Core.Configuration;
using EntraPimManager.Core.Graph;
using EntraPimManager.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting.Compact;

/// <summary>
/// Avalonia application composition root. Builds the generic host, wires the
/// tray icon to a single re-usable popup window, and prompts for first-run
/// configuration when none is present. There is no main window: the tray icon
/// is the only persistent UI anchor.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private TrayPopupController? _popupController;

    /// <summary>Service provider for views/code-behind that cannot get DI injection directly.</summary>
    public static IServiceProvider? Services { get; private set; }

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // The tray app has no main window — Avalonia must keep the message loop
        // running until we explicitly shut down via the tray menu.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _host = BuildHost();
            _host.Start();
        }
        catch (OptionsValidationException)
        {
            // No valid configuration yet. For Phase B we surface this on stderr
            // and exit; the first-run dialog comes in Phase C.
            Log.Fatal("Entra PIM Manager: no valid configuration. Set ClientId in appsettings.local.json.");
            desktop.Shutdown(1);
            return;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host startup failed");
            desktop.Shutdown(1);
            return;
        }

        Services = _host.Services;
        _popupController = Services.GetRequiredService<TrayPopupController>();

        // Load persisted user settings before any view model reads them.
        // Phases 2+ (theme apply, default duration, expiry-warn gating)
        // depend on the cached Current value being populated here.
        var userSettings = Services.GetRequiredService<IUserSettingsService>();
        userSettings.LoadAsync().GetAwaiter().GetResult();
        RequestedThemeVariant = ThemeMapper.ToVariant(userSettings.Current.Theme);

        SyncAutostartMenuCheck(this, Services);

        desktop.ShutdownRequested += (_, _) =>
        {
            _popupController?.PrepareForShutdown();
            _host?.StopAsync().GetAwaiter().GetResult();
            _host?.Dispose();
            Log.CloseAndFlush();
        };

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Tray icon click handler — wired from App.axaml.</summary>
    public void OnTrayIconClicked(object? sender, EventArgs e)
        => _popupController?.Toggle();

    /// <summary>Tray menu "Open" — wired from App.axaml.</summary>
    public void OnOpenClicked(object? sender, EventArgs e)
        => _popupController?.Show();

    /// <summary>Tray menu "Refresh" — wired from App.axaml.</summary>
    public async void OnRefreshClicked(object? sender, EventArgs e)
    {
        if (_popupController is not null)
        {
            await _popupController.RefreshAsync();
        }
    }

    /// <summary>Tray menu "Settings…" — opens the popup and slides the settings panel in.</summary>
    public void OnSettingsClicked(object? sender, EventArgs e)
    {
        _popupController?.Show();

        if (Services?.GetService(typeof(ShellViewModel)) is ShellViewModel shell)
        {
            shell.OpenSettingsPanelCommand.Execute(null);
        }
    }

    /// <summary>Tray menu "Start with Windows" — wired from App.axaml.</summary>
    public void OnAutostartClicked(object? sender, EventArgs e)
    {
        if (Services?.GetService(typeof(IAutostartService)) is not IAutostartService autostart)
        {
            return;
        }

        try
        {
            if (autostart.IsEnabled)
            {
                autostart.Disable();
            }
            else
            {
                autostart.Enable();
            }

            if (sender is NativeMenuItem item)
            {
                item.IsChecked = autostart.IsEnabled;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autostart toggle failed");
        }
    }

    /// <summary>Tray menu "Exit" — wired from App.axaml.</summary>
    public void OnExitClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static void SyncAutostartMenuCheck(Application app, IServiceProvider services)
    {
        if (services.GetService(typeof(IAutostartService)) is not IAutostartService autostart)
        {
            return;
        }

        var menu = TrayIcon.GetIcons(app)?.FirstOrDefault()?.Menu;
        if (menu is null)
        {
            return;
        }

        foreach (var entry in menu.Items)
        {
            if (entry is NativeMenuItem item
                && string.Equals(item.Header, "Start with Windows", StringComparison.Ordinal))
            {
                item.IsChecked = autostart.IsEnabled;
                break;
            }
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddJsonFile(LocalConfigStore.ConfigFilePath, optional: true, reloadOnChange: false);

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Entra-PIM-Manager",
            "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(logDirectory, "entra-pim-manager-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services.AddOptions<EntraPimManagerOptions>()
            .Bind(builder.Configuration.GetSection(EntraPimManagerOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<EntraPimManagerOptions>, EntraPimManagerOptionsValidator>();

        // Auth + Graph layer (Core).
        builder.Services.AddSingleton<IWindowTracker, AvaloniaWindowTracker>();
        builder.Services.AddSingleton<TokenCacheFactory>();
        builder.Services.AddSingleton<IAccountStore>(sp => new AccountStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Entra-PIM-Manager",
                "accounts.json"),
            sp.GetRequiredService<ILogger<AccountStore>>()));
        builder.Services.AddSingleton<IJustificationFavoritesStore>(sp => new JustificationFavoritesStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Entra-PIM-Manager",
                "favorites.json"),
            sp.GetRequiredService<ILogger<JustificationFavoritesStore>>()));
        builder.Services.AddSingleton<IUserSettingsService>(sp => new UserSettingsService(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Entra-PIM-Manager",
                "settings.json"),
            sp.GetRequiredService<ILogger<UserSettingsService>>()));
        builder.Services.AddSingleton<IAuthService, MsalAuthService>();
        builder.Services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        builder.Services.AddSingleton<PolicyCache>();
        builder.Services.AddSingleton<IAccountScopedServices, AccountScopedServices>();
        builder.Services.AddSingleton<IEligibilityAggregator, EligibilityAggregator>();
        builder.Services.AddSingleton<ITenantInfoService, TenantInfoService>();

        // UI + tray (App.Avalonia).
        builder.Services.AddSingleton<IToastService, ToastService>();
        builder.Services.AddSingleton<IAutostartService, AutostartService>();
        builder.Services.AddSingleton<ActivationPanelViewModel>();
        builder.Services.AddSingleton<AddTenantPanelViewModel>();
        builder.Services.AddSingleton<SettingsPanelViewModel>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<TrayPopupWindow>();
        builder.Services.AddSingleton<TrayPopupController>();

        return builder.Build();
    }
}
