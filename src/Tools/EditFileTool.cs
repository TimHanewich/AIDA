using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class EditFileTool : ExecutableFunction
    {
        public EditFileTool()
        {
            Name = "edit_file";
            Description = "Edit an existing file on the user's computer by replacing a specific string with a new string.";
            InputParameters.Add(new FunctionInputParameter("path", "The path of the file to edit."));
            InputParameters.Add(new FunctionInputParameter("old_string", "The existing string in the file to find and replace."));
            InputParameters.Add(new FunctionInputParameter("new_string", "The new string to replace the old string with."));
            InputParameters.Add(new FunctionInputParameter("replace_all", "If true, replace all occurrences. If false, only replace if the old_string is unique in the file. Defaults to true."));
        }

        public override Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string? path = null;
            string? old_string = null;
            string? new_string = null;
            bool replace_all = true;

            if (arguments != null)
            {
                JProperty? prop_path = arguments.Property("path");
                if (prop_path != null) path = prop_path.Value.ToString();

                JProperty? prop_old = arguments.Property("old_string");
                if (prop_old != null) old_string = prop_old.Value.ToString();

                JProperty? prop_new = arguments.Property("new_string");
                if (prop_new != null) new_string = prop_new.Value.ToString();

                JProperty? prop_replace_all = arguments.Property("replace_all");
                if (prop_replace_all != null)
                {
                    try { replace_all = bool.Parse(prop_replace_all.Value.ToString()); }
                    catch { replace_all = true; }
                }
            }

            if (path == null || old_string == null || new_string == null)
            {
                return Task.FromResult("You must provide 'path', 'old_string', and 'new_string' parameters!");
            }

            AnsiConsole.Markup("[gray][italic]editing '" + Markup.Escape(path) + "'... [/][/]");

            if (System.IO.File.Exists(path) == false)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return Task.FromResult("File at '" + path + "' does not exist!");
            }

            string content;
            try
            {
                content = System.IO.File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return Task.FromResult("There was an issue opening the file: " + ex.Message);
            }

            string[] split = old_string.Split(old_string);
            int occurences = split.Length - 1;
            if (occurences == 0)
            {
                AnsiConsole.MarkupLine("[gray][italic]no changes[/][/]");
                return Task.FromResult("No edits made. There were no occurences of the old_string you provided in the file content.");
            }
            else if (occurences > 1)
            {
                if (replace_all == false)
                {
                    AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                    return Task.FromResult("There are multiple occurences of the old_string you provided in the file content and you did not have 'replace_all' enabled. Please expand the context of the 'old_string' (make it unique) to clearly indicate what portion you want to edit.");
                }
            }

            content = content.Replace(old_string, new_string);
            System.IO.File.WriteAllText(path, content);

            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return Task.FromResult("File edit was successful.");
        }
    }
}
