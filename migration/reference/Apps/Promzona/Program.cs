using System.Globalization;
namespace Promzona;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            string? saveDirectory=null, importFile=null;
            for(int i=0;i<args.Length;i++) {
                if(i+1==args.Length)throw new ArgumentException("Option requires a value: "+args[i]);
                switch(args[i++]) {
                    case "--language": language=args[i];break;
                    case "--save-directory": saveDirectory=Path.GetFullPath(args[i]);break;
                    case "--import-save": importFile=Path.GetFullPath(args[i]);break;
                    default: throw new ArgumentException("Usage: Promzona.exe [--language en|ru] [--import-save file.json] [--save-directory folder]");
                }
            }
            if(importFile is not null)GameSession.ImportSave(importFile,saveDirectory);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var window = new GameWindow(AppContext.BaseDirectory, language,saveDirectory:saveDirectory);
            Application.Run(window);
            return window.ExitCode;
        }
        catch (Exception ex)
        {
            using var fault = new Form { Text = "Promzona", BackColor = Color.Black, ForeColor = Color.Silver, ClientSize = new(700, 300) };
            fault.Controls.Add(new Label { Dock = DockStyle.Fill, BackColor = Color.Black, ForeColor = Color.Silver, Text = ex.Message, Padding = new Padding(30) });
            Application.Run(fault);
            return 1;
        }
    }
}
