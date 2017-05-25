using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace webpconv {
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();
            DataContext = vm;
        }
        public static RoutedCommand Start = new RoutedCommand();
        public static RoutedCommand Cancel = new RoutedCommand();
        ViewModel vm = new ViewModel();
        CancellationTokenSource cts;
        ParallelOptions po;
        const string encode_arg = @"/c cwebp -quiet -lossless -z 9";
        const string decode_arg = @"/c dwebp";
        int total = 0, current = 0;

        private async void Start_Executed(object sender, ExecutedRoutedEventArgs e) {
            using (var dialog = new CommonOpenFileDialog() { IsFolderPicker = true }) {
                if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;
                vm.Idle = false;
                vm.DeltaText = null;
                cts = new CancellationTokenSource();
                po = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token };
                Action A;
                switch (vm.Index) {
                    default:
                        A = Encode(dialog.FileName);
                        break;
                    case 1:
                        A = Decode(dialog.FileName);
                        break;
                    case 2:
                        A = ReEncode(dialog.FileName);
                        break;
                }
                await CreateTask(A);
            }
        }
        private void Cancel_Executed(object sender, ExecutedRoutedEventArgs e) {
            if (MessageBox.Show("Are you sure to cancel?", "AudioSync", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            CancelButton.IsEnabled = false;
            cts.Cancel();
        }

        Action Encode(string path) => () => {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".bmp") || f.EndsWith(".png") || f.EndsWith(".tif"));
            total = files.Count();
            long TotalDelta = 0;
            vm.Ptext = $"0 / {total}";
            Parallel.ForEach(files, po, f => {
                try {
                    var outf = Path.ChangeExtension(f, ".webp");
                    if (File.Exists(outf)) File.Delete(outf);
                    Process.Start(Psi($"{encode_arg} {f.WQ()} -o {outf.WQ()}")).WaitForExit();
                    FileInfo fiI = new FileInfo(f), fiO = new FileInfo(outf);
                    if (fiO.Length > 0) {
                        var delta = fiI.Length - fiO.Length;
                        if (delta != 0) {
                            Interlocked.Add(ref TotalDelta, fiI.Length - fiO.Length);
                            vm.DeltaText = $"{FileSizeHelpler.SizeSuffix(TotalDelta)} decreased";
                        }
                        fiI.IsReadOnly = false;
                        fiI.Delete();
                        Interlocked.Increment(ref current);
                    }
                    else MessageBox.Show($"error on: {f}");
                    vm.Pvalue = (double)current / total;
                    vm.Ptext = $"{current} / {total}";
                }
                catch (Exception ex) {
                    MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {f}");
                }
            });
        };

        Action Decode(string path) => () => {
            var files = Directory.GetFiles(path, "*.webp", SearchOption.AllDirectories);
            total = files.Length;
            vm.Ptext = $"0 / {total}";
            Parallel.ForEach(files, po, f => {
                try {
                    var outf = Path.ChangeExtension(f, ".png");
                    if (File.Exists(outf)) File.Delete(outf);
                    Process.Start(Psi($"{decode_arg} {f.WQ()} -o {outf.WQ()}")).WaitForExit();
                    Interlocked.Increment(ref current);
                    vm.Pvalue = (double)current / total;
                    vm.Ptext = $"{current} / {total}";
                }
                catch (Exception ex) {
                    MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {f}");
                }
            });
        };

        Action ReEncode(string path) => () => {
            var files = Directory.GetFiles(path, "*.webp", SearchOption.AllDirectories);
            total = files.Length;
            long TotalDelta = 0;
            vm.Ptext = $"0 / {total}";
            Parallel.ForEach(files, po, f => {
                try {
                    var tempf = Path.Combine(Directory.GetParent(f).FullName, $"{Guid.NewGuid()}.webp");
                    Process.Start(Psi($"{encode_arg} {f.WQ()} -o {tempf.WQ()}")).WaitForExit();
                    FileInfo fiI = new FileInfo(f), fiT = new FileInfo(tempf);
                    if (fiT.Length > 0) {
                        var delta = fiI.Length - fiT.Length;
                        if (delta != 0) Interlocked.Add(ref TotalDelta, fiI.Length - fiT.Length);
                        vm.DeltaText = $"{FileSizeHelpler.SizeSuffix(TotalDelta)} decreased";
                        fiI.IsReadOnly = false;
                        fiI.Delete();
                        fiT.MoveTo(f);
                        Interlocked.Increment(ref current);
                    }
                    else MessageBox.Show($"error on: {f}");
                    vm.Pvalue = (double)current / total;
                    vm.Ptext = $"{current} / {total}";
                }
                catch (Exception ex) {
                    MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {f}");
                }
            });
        };

        Task CreateTask(Action action) => Task.Run(() => {
            try {
                action();
            }
            catch (Exception ex) {
                MessageBox.Show(ex.Message);
            }
            finally {
                SystemSounds.Asterisk.Play();
                if (!cts.IsCancellationRequested) MessageBox.Show("complete");
                cts.Dispose();
                vm.Pvalue = total = current = 0;
                vm.Ptext = null;
                vm.Idle = true;
            }
        });

        ProcessStartInfo Psi(string arg) => new ProcessStartInfo() { FileName = "cmd.exe", Arguments = arg, UseShellExecute = false, CreateNoWindow = true };
    }
}
