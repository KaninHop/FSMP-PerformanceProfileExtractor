using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using FSMPLogVisualizer.Core;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FSMPLogVisualizer.UI;

public partial class MainWindow : Window
{
    private LogRepository _repo;
    private LogParser _parser;

    public MainWindow()
    {
        InitializeComponent();
        
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FSMPLogVisualizer.db");
        _repo = new LogRepository(dbPath);
        _parser = new LogParser();
        
        ApplyPlotTheme();

        // Re-apply theme when system theme changes
        ActualThemeVariantChanged += (_, _) => ApplyPlotTheme();

        Task.Run(() => LoadAndPlotExistingData());
    }

    private void ApplyPlotTheme()
    {
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;

        var bgColor      = isDark ? ScottPlot.Color.FromHex("#1C1F2E") : ScottPlot.Color.FromHex("#FFFFFF");
        var fgColor      = isDark ? ScottPlot.Color.FromHex("#9BA8C8") : ScottPlot.Color.FromHex("#4A5568");
        var gridColor    = isDark ? ScottPlot.Color.FromHex("#252840") : ScottPlot.Color.FromHex("#E9EDF4");
        var legendBg     = isDark ? ScottPlot.Color.FromHex("#1C1F2E") : ScottPlot.Color.FromHex("#FFFFFF");
        var legendFg     = isDark ? ScottPlot.Color.FromHex("#C8D0E8") : ScottPlot.Color.FromHex("#2D3748");
        var legendBorder = isDark ? ScottPlot.Color.FromHex("#343860") : ScottPlot.Color.FromHex("#DDE2EF");

        foreach (var avaPlot in new[] { CostPlot, PerSkelPlot })
        {
            avaPlot.Plot.FigureBackground.Color  = bgColor;
            avaPlot.Plot.DataBackground.Color    = bgColor;
            avaPlot.Plot.Axes.Color(fgColor);
            avaPlot.Plot.Grid.MajorLineColor = gridColor;

            // Legend theming
            avaPlot.Plot.Legend.BackgroundColor  = legendBg;
            avaPlot.Plot.Legend.FontColor        = legendFg;
            avaPlot.Plot.Legend.OutlineColor     = legendBorder;

            avaPlot.Plot.XLabel("Active Skeletons");
            avaPlot.Plot.Axes.Bottom.Label.FontSize = 12;
            avaPlot.Plot.Axes.Left.Label.FontSize   = 12;

            // Disable mouse zoom / pan — charts are read-only views
            avaPlot.UserInputProcessor.IsEnabled = false;
        }

        CostPlot.Plot.YLabel("Cost (ms)");
        PerSkelPlot.Plot.YLabel("Cost per Skeleton (ms)");

        CostPlot.Refresh();
        PerSkelPlot.Refresh();
    }

    private async Task LoadAndPlotExistingData()
    {
        try
        {
            var sessions = await _repo.GetSessionsAsync();
            var allPoints = await _repo.GetAllDataPointsAsync();
            
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateCharts(sessions, allPoints));
        }
        catch (Exception)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText.Text = "Error loading data.");
        }
    }

    private async void OnAddLogsClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Select log files...";
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select HDT-SMP Logs",
            AllowMultiple = true
        });

        if (result != null && result.Count > 0)
        {
            StatusText.Text = "Processing logs...";
            foreach (var file in result)
            {
                var (session, dataPoints) = await _parser.ParseLogFileAsync(file.Path.LocalPath);
                
                if (!await _repo.SessionExistsAsync(session.SessionKey))
                {
                    await _repo.SaveSessionAsync(session);
                    foreach(var dp in dataPoints) dp.SessionId = session.Id;
                    await _repo.SaveDataPointsAsync(dataPoints);
                }
            }
            StatusText.Text = "Processing complete. Updating charts...";
            await LoadAndPlotExistingData();
            StatusText.Text = "Ready";
        }
        else
        {
            StatusText.Text = "Ready";
        }
    }

    private async void OnClearDataClick(object sender, RoutedEventArgs e)
    {
        await _repo.ClearAllAsync();
        StatusText.Text = "Data cleared.";
        await LoadAndPlotExistingData();
    }

    private async void OnAutoLoadClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Autoloading logs...";
        
        string sksePath = OperatingSystem.IsWindows() 
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Skyrim Special Edition", "SKSE")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam", "steamapps", "compatdata", "489830", "pfx", "drive_c", "users", "steamuser", "Documents", "My Games", "Skyrim Special Edition", "SKSE");

        if (Directory.Exists(sksePath))
        {
            var files = Directory.GetFiles(sksePath, "*.log").Where(f => f.Contains("hdtsmp64", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (files.Length == 0)
            {
                StatusText.Text = "No hdtSMP64 logs found in SKSE folder.";
                return;
            }

            foreach (var file in files)
            {
                var (session, dataPoints) = await _parser.ParseLogFileAsync(file);
                
                if (!await _repo.SessionExistsAsync(session.SessionKey))
                {
                    await _repo.SaveSessionAsync(session);
                    foreach(var dp in dataPoints) dp.SessionId = session.Id;
                    await _repo.SaveDataPointsAsync(dataPoints);
                }
            }
            StatusText.Text = "Autoload complete. Updating charts...";
            await LoadAndPlotExistingData();
            StatusText.Text = "Ready";
        }
        else
        {
            StatusText.Text = "SKSE path not found!";
        }
    }

    private void UpdateCharts(List<LogRunSession> sessions, List<LogDataPoint> allPoints)
    {
        CostPlot.Plot.Clear();
        PerSkelPlot.Plot.Clear();

        var sessionVersions = sessions.ToDictionary(s => s.Id, s => s.Version);
        var pointsWithVersion = allPoints.Select(p => new { Point = p, Version = sessionVersions.ContainsKey(p.SessionId) ? sessionVersions[p.SessionId] : "Unknown" });
        
        var groupedByVersion = pointsWithVersion.GroupBy(p => p.Version).ToList();

        int sessionIndex = 0;
        int numSessions = groupedByVersion.Count;
        double barWidth = 0.8 / (numSessions == 0 ? 1 : numSessions);
        var palette = new ScottPlot.Palettes.Category10();

        foreach (var versionGroup in groupedByVersion)
        {
            string legendName = versionGroup.Key;
            
            double offset = (sessionIndex - (numSessions - 1) / 2.0) * barWidth;
            
            var mainColor = palette.GetColor(sessionIndex % palette.Colors.Length);
            var outsideColor = mainColor.WithAlpha(150);

            var sessPoints = versionGroup.Select(v => v.Point);

            var validPoints = sessPoints.Where(p => p.ActiveSkeletons.HasValue && p.CostInMainLoop.HasValue && p.CostOutsideMainLoop.HasValue)
                                            .GroupBy(p => p.ActiveSkeletons.Value)
                                            .OrderBy(g => g.Key)
                                            .ToList();

            if (validPoints.Any())
            {
                var mainBars = new List<Bar>();
                var outsideBars = new List<Bar>();
                var perSkelBars = new List<Bar>();

                foreach (var g in validPoints)
                {
                    double xPos = g.Key + offset;
                    double avgMainCost = g.Average(p => p.CostInMainLoop.Value);
                    double avgOutsideCost = g.Average(p => p.CostOutsideMainLoop.Value);
                    double avgTotalCost = avgMainCost + avgOutsideCost;
                    
                    mainBars.Add(new Bar { Position = xPos, ValueBase = 0, Value = avgMainCost, FillColor = mainColor, Size = barWidth });
                    outsideBars.Add(new Bar { Position = xPos, ValueBase = avgMainCost, Value = avgTotalCost, FillColor = outsideColor, Size = barWidth });

                    double perSkelValue = g.Key > 0 ? avgTotalCost / g.Key : 0;
                    perSkelBars.Add(new Bar { Position = xPos, ValueBase = 0, Value = perSkelValue, FillColor = mainColor, Size = barWidth });
                }

                var mainBarPlot = CostPlot.Plot.Add.Bars(mainBars);
                mainBarPlot.LegendText = $"{legendName} - Main Loop";

                var outsideBarPlot = CostPlot.Plot.Add.Bars(outsideBars);
                outsideBarPlot.LegendText = $"{legendName} - Outside";

                var perSkelPlot = PerSkelPlot.Plot.Add.Bars(perSkelBars);
                perSkelPlot.LegendText = legendName;
            }
            sessionIndex++;
        }

        CostPlot.Plot.ShowLegend(Alignment.UpperLeft);
        PerSkelPlot.Plot.ShowLegend(Alignment.UpperRight);

        // Autoscale to fit data, then pin Y baseline to 0 so bars sit on the axis
        CostPlot.Plot.Axes.AutoScale();
        var costLimits = CostPlot.Plot.Axes.GetLimits();
        CostPlot.Plot.Axes.SetLimitsY(0, costLimits.Top);

        PerSkelPlot.Plot.Axes.AutoScale();
        var perSkelLimits = PerSkelPlot.Plot.Axes.GetLimits();
        PerSkelPlot.Plot.Axes.SetLimitsY(0, perSkelLimits.Top);

        CostPlot.Refresh();
        PerSkelPlot.Refresh();
    }

    private async void OnDownloadRawClick(object sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Raw Data as CSV",
            SuggestedFileName = "fsmp_performance_data",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV Spreadsheet") { Patterns = ["*.csv"] }]
        });

        if (result != null)
        {
            var sessions = await _repo.GetSessionsAsync();
            var allPoints = await _repo.GetAllDataPointsAsync();
            
            using var stream = await result.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync("SessionId,Version,Timestamp,ActiveSkeletons,MaxActive,TotalSkeletons,CostInMainLoop,CostOutsideMainLoop,PercentageOutside,ProcessTimeInMainLoop,TargetTime");
            
            foreach(var p in allPoints)
            {
                var sInfo = sessions.FirstOrDefault(s => s.Id == p.SessionId);
                var v = sInfo?.Version ?? "";
                await writer.WriteLineAsync($"{p.SessionId},{v},{p.Timestamp},{p.ActiveSkeletons},{p.MaxActiveSkeletons},{p.TotalSkeletons},{p.CostInMainLoop},{p.CostOutsideMainLoop},{p.PercentageOutside},{p.ProcessTimeInMainLoop},{p.TargetTime}");
            }
        }
    }

    private async void OnDownloadChartsClick(object sender, RoutedEventArgs e)
    {
        var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Combined Charts Image",
            SuggestedFileName = "fsmp_charts",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }]
        });

        if (result != null)
        {
            try
            {
                // Render the two-chart grid (the Grid in column 1 of root)
                var contentRoot = this.Content as Grid;   // root ColumnDefinitions="210,*"
                if (contentRoot != null)
                {
                    var mainArea = contentRoot.Children[1] as Grid; // the right-side Grid
                    if (mainArea != null)
                    {
                        // Charts sit in row 1 of mainArea
                        var chartsGrid = mainArea.Children[1] as Grid;
                        if (chartsGrid != null)
                        {
                            var rect = chartsGrid.Bounds;
                            var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(
                                new Avalonia.PixelSize((int)rect.Width, (int)rect.Height),
                                new Avalonia.Vector(96, 96));
                            rtb.Render(chartsGrid);
                            using var stream = await result.OpenWriteAsync();
                            rtb.Save(stream);
                        }
                    }
                }
            }
            catch { /* silently ignore render issues */ }
        }
    }
}