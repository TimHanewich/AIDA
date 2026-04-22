using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class WriteFileTool : ExecutableFunction
    {
        public WriteFileTool()
        {
            Name = "write_file";
            Description = "Create a new file on the user's computer in the current directory.";
            InputParameters.Add(new FunctionInputParameter("path", "The path of the file to write."));
            InputParameters.Add(new FunctionInputParameter("content", "The content of the file."));
        }

        public override Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string file_name = "dummy.txt";
            string file_content = "(dummy content)";

            if (arguments != null)
            {
                JProperty? prop_path = arguments.Property("path");
                if (prop_path != null) file_name = prop_path.Value.ToString();

                JProperty? prop_content = arguments.Property("content");
                if (prop_content != null) file_content = prop_content.Value.ToString();
            }

            AnsiConsole.Markup("[gray][italic]writing '" + Markup.Escape(file_name) + "'... [/][/]");

            string? DestinationDirectory = Path.GetDirectoryName(file_name);
            if (DestinationDirectory == null)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return Task.FromResult("Unable to determine destination directory from the path you provided. Are you sure it is valid?");
            }
            if (DestinationDirectory != "" && Directory.Exists(DestinationDirectory) == false)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return Task.FromResult("Path invalid! Destination directory does not exist");
            }

            System.IO.File.WriteAllText(file_name, file_content);
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return Task.FromResult("File successfully saved to '" + file_name + "'.");
        }
    }
}
