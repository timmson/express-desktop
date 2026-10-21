namespace ExpressApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// AppShell resolving через DI, чтобы MainPage получил сервисы авторизации
		return new Window(MauiProgram.Services.GetRequiredService<AppShell>());
	}
}
