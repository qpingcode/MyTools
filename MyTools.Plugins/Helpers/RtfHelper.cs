using System.IO;
using System.Text;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Media;

namespace MyTools.Plugins
{
    public static class RtfHelper
    {
        public static (string title, string hyperLink) RtfToPlainText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return (string.Empty, string.Empty);
            }

            try
            {
                var flowDocument = new FlowDocument();
                var textRange = new TextRange(flowDocument.ContentStart, flowDocument.ContentEnd);

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text ?? string.Empty)))
                {
                    textRange.Load(stream, DataFormats.Rtf);
                }
                
                foreach (var block in flowDocument.Blocks)
                {
                    if (block is Paragraph paragraph)
                    {
                        foreach (var inline in paragraph.Inlines)
                        {
                            if (inline is Hyperlink hyperlink)
                            {
                                string? url = hyperlink.NavigateUri?.ToString();
                                string title = new TextRange(hyperlink.ContentStart, hyperlink.ContentEnd).Text.Trim();
                                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title))
                                {
                                    return (title, url);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error converting RTF to plain text: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            return (string.Empty, string.Empty);
        }
    }
}