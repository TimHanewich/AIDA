using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.IO.Compression;

namespace AIDA
{
    public class ReadFileTool : ExecutableFunction
    {
        public ReadFileTool()
        {
            Name = "read_file";
            Description = "Read the contents of a file of any type (txt, pdf, word document, etc.) from the user's computer";
            InputParameters.Add(new FunctionInputParameter("file_path", "The path to the file on the computer, for example 'C:\\Users\\timh\\Downloads\\notes.txt' or '.\\notes.txt' or 'notes.txt'"));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string file_path = "?";
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("file_path");
                if (prop != null) file_path = prop.Value.ToString();
            }

            AnsiConsole.Markup("[gray][italic]reading '" + Markup.Escape(file_path) + "'... [/][/]");
            string result = ReadFile(file_path);
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return result;
        }

        private static string ReadFile(string path)
        {
            if (System.IO.File.Exists(path) == false)
            {
                return "File with path '" + path + "' does not exist!";
            }

            if (path.ToLower().EndsWith(".pdf"))
            {
                string FullTxt = "";
                PdfDocument doc = PdfDocument.Open(path);
                foreach (UglyToad.PdfPig.Content.Page p in doc.GetPages())
                {
                    string txt = ContentOrderTextExtractor.GetText(p);
                    FullTxt = FullTxt + txt + "\n\n";
                }
                if (FullTxt.Length > 0)
                {
                    FullTxt = FullTxt.Substring(0, FullTxt.Length - 2);
                }
                return FullTxt;
            }
            else if (path.ToLower().EndsWith(".zip"))
            {
                return "Cannot read the raw content of a .zip folder!";
            }
            else if (path.ToLower().EndsWith(".docx") || path.ToLower().EndsWith(".doc"))
            {
                return ReadWordDocument(path);
            }
            else if (path.ToLower().EndsWith(".xlsx") || path.ToLower().EndsWith(".xls"))
            {
                return "Cannot read an excel document";
            }
            else if (path.ToLower().EndsWith(".pptx") || path.ToLower().EndsWith(".ppt"))
            {
                return "Cannot read a PowerPoint deck.";
            }
            else
            {
                return System.IO.File.ReadAllText(path);
            }
        }

        private static string ReadWordDocument(string path)
        {
            string RawXmlContent = "";
            try
            {
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                MemoryStream ms = new MemoryStream();
                fs.CopyTo(ms);
                ZipArchive za = new ZipArchive(ms, ZipArchiveMode.Read);
                foreach (ZipArchiveEntry zae in za.Entries)
                {
                    if (zae.FullName == "word/document.xml")
                    {
                        Stream EntryStream = zae.Open();
                        StreamReader sr = new StreamReader(EntryStream);
                        string RawText = sr.ReadToEnd();
                        RawXmlContent = RawText;
                    }
                }
            }
            catch (Exception ex)
            {
                return "There was an error while trying to open word document '" + path + "'. Exception message: " + ex.Message;
            }

            string ToReturn = "";
            if (RawXmlContent == "")
            {
                ToReturn = "Unable to read Word document content.";
            }
            else
            {
                string[] parts = RawXmlContent.Split("<w:t>", StringSplitOptions.None);
                for (int t = 1; t < parts.Length; t++)
                {
                    string ThisPart = parts[t];
                    int ClosingTagLocation = ThisPart.IndexOf("</w:t>");
                    if (ClosingTagLocation > -1)
                    {
                        string TextContent = ThisPart.Substring(0, ClosingTagLocation);
                        ToReturn = ToReturn + TextContent + "\n";
                    }
                }
                ToReturn = ToReturn.TrimEnd('\n');
            }

            return ToReturn;
        }
    }
}
