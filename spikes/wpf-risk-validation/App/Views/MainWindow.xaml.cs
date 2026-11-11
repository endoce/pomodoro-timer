using System.ComponentModel;
using System.Windows;

namespace PomodoroTimer.Spike.Views;

public partial class MainWindow : Window
{
    public bool AllowClose { get; set; }
    public MainWindow() => InitializeComponent();
    protected override void OnClosing(CancelEventArgs args)
    {
        // Application shutdown is explicit; a window close never destroys timer state.
        if (!AllowClose)
        {
            args.Cancel = true;
            Hide();
        }
        base.OnClosing(args);
    }
}
