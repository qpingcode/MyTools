using System.Windows;
using System.Windows.Input;
using MyTools.Desktop.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace MyTools.Desktop.Views
{
    public partial class HotKeyEditorWindow
    {
        private readonly HotKeyEditorViewModel _viewModel;
        private bool _isEditing = false;

        public HotKeyEditorWindow(HotKeyConfig currentHotKey)
        {
            InitializeComponent();

            _viewModel = new HotKeyEditorViewModel(currentHotKey);
            DataContext = _viewModel;
            HotKeyTextBox.Text = "请按下快捷键...";
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_isEditing)
            {
                _isEditing = true;
                HotKeyTextBox.Text = string.Empty;
            }
            
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (_viewModel.IsValidHotKey())
                {
                    DialogResult = true;
                    Close();
                }
                return;
            }

            _viewModel.HandleKeyDown(e);
            HotKeyTextBox.Text = _viewModel.HotKeyText;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab || e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IsValidHotKey())
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("请设置有效的快捷键组合", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}