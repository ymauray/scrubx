using System.Windows;
using Microsoft.AspNetCore.Builder;

namespace Scrubx.Desktop;

public partial class MainWindow : Window
{
    private WebApplication? _app;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Port 0 : le système attribue un port libre, l'appli reste strictement locale
        // (pas de serveur exposé sur le réseau, pas de configuration à faire).
        // Réglé via IConfiguration (et non UseUrls) car appsettings.json de Scrubx.Web
        // est recopié à côté de l'exe par MSBuild (propagation transitive des Content
        // items via la référence de projet) et son Kestrel:Endpoints:Http:Url fixe
        // (127.0.0.1:5099) prendrait autrement le dessus sur UseUrls.
        _app = Scrubx.Web.WebAppFactory.Create(
            [],
            builder => builder.Configuration["Kestrel:Endpoints:Http:Url"] = "http://127.0.0.1:0");

        await _app.StartAsync();

        var address = _app.Urls.First();

        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.Navigate(address);
    }
}
