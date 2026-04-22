using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class DeleteFileTool : ExecutableFunction
    {
        public DeleteFileTool()
        {
            Name = "delete_file";
            Description = "Delete a file from the user's computer.";
            InputParameters.Add(new FunctionInputParameter("path", "The path of the file to delete."));
        }

        public override Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string? path = null;
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("path");
                if (prop != null) path = prop.Value.ToString();
            }

            if (path == null)
            {
                return Task.FromResult("You must provide the 'path' parameter!");
            }
            if (System.IO.File.Exists(path) == false)
            {
                return Task.FromResult("File at '" + path + "' does not exist!");
            }

            try
            {
                AnsiConsole.Markup("[gray][italic]deleting '" + Markup.Escape(path) + "'... [/][/]");
                System.IO.File.Delete(path);
                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
                return Task.FromResult("File '" + path + "' was successfully deleted.");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return Task.FromResult("Deletion of file failed. Exception message: " + ex.Message);
            }
        }
    }
}
