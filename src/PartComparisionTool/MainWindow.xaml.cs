using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PartComparisionTool.Models;
using PartComparisionTool.Services;

namespace PartComparisionTool
{
    public partial class MainWindow : Window
    {
        private PartComparisonService _service;

        public MainWindow()
        {
            InitializeComponent();
            InitialiseService();
        }

        private void InitialiseService()
        {
            try
            {
                _service = new PartComparisonService();
                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                ConnectionText.Text = "Not connected";
                StatusText.Text = ex.Message;
            }
        }

        private void UpdateConnectionStatus()
        {
            ConnectionText.Text = _service != null && _service.IsConnected
                ? "Connected to Tekla 2023"
                : "Not connected - open Tekla Structures 2023 with a model loaded";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            InitialiseService();
        }

        private void SetTargetButton_Click(object sender, RoutedEventArgs e)
        {
            RunSafely(() =>
            {
                TargetText.Text = _service.CaptureTargetSelection();
                SearchSelectionText.Text = "No search selection processed yet.";
                ResultsGrid.ItemsSource = null;
                StatusText.Text = "Target stored. Now select the steel you want to search through.";
            }, false);
        }

        private void SearchSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            RunSafely(() =>
            {
                ResultsGrid.ItemsSource = null;

                string searchDescription;
                var results = _service.FindMatchesInCurrentSelection(UpdateProgress, out searchDescription);

                SearchSelectionText.Text = searchDescription;
                ResultsGrid.ItemsSource = results;

                if (results.Count == 0)
                {
                    StatusText.Text = "No same-profile / same-length candidates were found in the selected steel.";
                }
                else
                {
                    var exactCount = results.Count(x => x.Quality == MatchQuality.Exact);
                    StatusText.Text = exactCount > 0
                        ? $"Found {results.Count:n0} candidate(s), including {exactCount:n0} exact match(es)."
                        : $"Found {results.Count:n0} candidate(s). No exact match was found; closest options are shown first.";
                }
            });
        }

        private void SelectResultButton_Click(object sender, RoutedEventArgs e)
        {
            SelectCurrentResult();
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCurrentResult();
        }

        private void SelectCurrentResult()
        {
            RunSafely(() =>
            {
                var result = ResultsGrid.SelectedItem as ComparisonResult;
                if (result == null)
                    throw new InvalidOperationException("Select a result first.");

                _service.SelectResult(result);
                StatusText.Text = $"Selected {result.AssemblyMark} in Tekla.";
            }, false);
        }

        private void UpdateProgress(SearchProgress progress)
        {
            if (progress == null)
                return;

            StatusText.Text = progress.Message ?? string.Empty;

            if (progress.Total > 0)
            {
                SearchProgressBar.IsIndeterminate = false;
                SearchProgressBar.Maximum = progress.Total;
                SearchProgressBar.Value = Math.Min(progress.Total, Math.Max(0, progress.Current));
            }
            else
            {
                SearchProgressBar.IsIndeterminate = true;
            }

            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        private void RunSafely(Action action, bool resetProgress = true)
        {
            try
            {
                SetTargetButton.IsEnabled = false;
                SearchSelectionButton.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;

                if (resetProgress)
                {
                    SearchProgressBar.IsIndeterminate = true;
                    SearchProgressBar.Value = 0;
                }

                action();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            finally
            {
                SearchProgressBar.IsIndeterminate = false;
                SetTargetButton.IsEnabled = true;
                SearchSelectionButton.IsEnabled = true;
                Mouse.OverrideCursor = null;
                UpdateConnectionStatus();
            }
        }
    }
}
