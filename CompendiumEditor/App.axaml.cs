// File: App.axaml.cs
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CompendiumEditor.Services.Configuration;
using CompendiumEditor.Services.Data;
using CompendiumEditor.Services.Logging;
using CompendiumEditor.ViewModels;
using CompendiumEditor.Views;
using System.Threading.Tasks;

namespace CompendiumEditor
{

public partial class App : Application
{
    /// <summary>
    /// Global read-only gateway to access resolved application dependencies securely across cross-platform contexts.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // 1. Initialize Dependency Injection Container Mappings
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        Services = serviceCollection.BuildServiceProvider();

        // 2. Load User Environment Preferences Prior to Visual Presentation
        var config = Services.GetRequiredService<IConfigurationService>();
        config.LoadSettings();

        // 3. Apply Theme Variant based on configuration
        RequestedThemeVariant = config.ThemeMode == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        // 4. Bind MainWindow or MainView across App Life Cycle
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
                var splash = new SplashScreen();
                desktop.MainWindow = splash;
                splash.Show();

                // Wait for 3 seconds to show the logo
                await System.Threading.Tasks.Task.Delay(2000);

                var mainWin = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainWindowViewModel>(),
                };
                
                desktop.MainWindow = mainWin;
                mainWin.Show();
                splash.Close();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Core configuration grid declaring concrete types mapping onto abstract service facades.
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        // --- Core Infrastructure & Services Layer ---
        services.AddSingleton<IDiagnosticLogger, DiagnosticLogger>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<ICompendiumExtractor, CompendiumExtractor>();
        services.AddSingleton<IPreviewStylingService, PreviewStylingService>();
        
        // --- Compendium Writer Strategy Pattern ---
        services.AddSingleton<GeneralCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, MonsterCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, GlossaryCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, ClassCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, ArmorCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, BackgroundCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, CompanionCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, PowerCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, FeatCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, ItemCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, TrapCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, WeaponCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, ImplementCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, ParagonPathCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, RitualCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, DeityCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, ThemeCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, EpicDestinyCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, DiseaseCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, RaceCompendiumWriter>();
        services.AddSingleton<ICategoryCompendiumWriter, PoisonCompendiumWriter>();
        services.AddSingleton<ICompendiumWriter, CompendiumWriterDispatcher>();

        // --- Presentation ViewModels Layer ---
        services.AddTransient<MainWindowViewModel>();

        // --- Visual View Elements Layer ---
        services.AddTransient<MainWindow>(sp => new MainWindow
        {
            DataContext = sp.GetRequiredService<MainWindowViewModel>()
        });
    }
}
}