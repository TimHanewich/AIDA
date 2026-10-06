using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;
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
            string result = await ReadFileAsync(file_path);
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return result;
        }

        private static async Task<string> ReadFileAsync(string path)
        {
            if (System.IO.File.Exists(path) == false)
            {
                return "File with path '" + path + "' does not exist!";
            }

            string[] markitdown_extensions = new string[]{".docx", ".pptx", ".xlsx", ".pdf"};

            //DECLARE TO RETURN
            string TORETURN = "";

            //Get file type extensions
            string ext = Path.GetExtension(path); //returns like ".txt" for example (with the dot)

            //Do it by markitdown? If not, plain text
            if (markitdown_extensions.Contains(ext))
            {
                //Check if python is installed AND if markitdown is installed with simple test to import!
                string response = await Tools.ExecuteShellAsync("python -c \"import markitdown\"");

                //If it worked, it will return absolutley nothing
                if (response != "")
                {
                    return "Unable to read the file: python and markitdown must be installed for the ability to read files of that extension type. Please instruct the user to install these.";
                }

                //Prepare a temp file to read into
                string file_name = Path.GetFileNameWithoutExtension(path); //Get the file name
                string file_name_temp = file_name + "_temp_" + Guid.NewGuid().ToString().Replace("-", "").Substring(0, 5) + ".md"; //Create a temp file name for reading into.
                string dir_path = Path.GetDirectoryName(path)!;
                string full_temp_path = Path.Combine(dir_path, file_name_temp);

                //Run command and use markitdown to convert and save in that temporary file
                string COMMAND = "python -m markitdown \"" + path + "\" -o \"" + full_temp_path + "\"";
                string RESPONSE = await Tools.ExecuteShellAsync(COMMAND);

                //Handle if it worked as expected...
                if (File.Exists(full_temp_path))
                {
                    string content = System.IO.File.ReadAllText(full_temp_path);
                    if (content != null && content != "")
                    {
                        //It worked!

                        //First delete the temp file
                        System.IO.File.Delete(full_temp_path);

                        //Return it
                        TORETURN = content;
                    }
                }

                //If we got down to here, that means it didn't go as planned. This is the fallback!

                //If the temp file exists, still delete it (clean up)
                if (File.Exists(full_temp_path))
                {
                    File.Delete(full_temp_path);
                }

                //Return command results
                return "Reading of file '" + path + "' was unsuccessfull. Here was the direct output of the attempt to use markitdown to convert it: '" + RESPONSE + "'.";
            }
            else //assume it is plain text related (like .txt or .md for example)
            {
                TORETURN = System.IO.File.ReadAllText(path);
            }

            //Before returning, check maximum
            //max chars = 10,485,760
            //See this error: https://i.imgur.com/8jHB247.png
            if (TORETURN.Length > 10_000_000)
            {
                return "Content of file '" + path + "' was " + TORETURN.Length.ToString("#,##0") + " characters and that is too long to provide back.";
            }
            return TORETURN;

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
