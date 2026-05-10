using System;
using System.Data;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using TimHanewich.AgentFramework;

namespace AIDA
{
    public class CalculateTool : ExecutableFunction
    {
        public CalculateTool()
        {
            Name = "calculate";
            Description = "Evaluate a math expression and return the result.";
            InputParameters.Add(new TimHanewich.Foundry.OpenAI.Responses.FunctionInputParameter("expression", "The math expression to evaluate, for example '152 * 1.08' or '(500 + 300) / 4'"));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            if (arguments == null)
            {
                return "Must provide arguments.";
            }

            //Get expression
            JProperty? prop_expression = arguments.Property("expression");
            if (prop_expression == null)
            {
                return "Must provide parameter 'expression'.";
            }
            string expression = prop_expression.Value.ToString();

            //Print it
            AnsiConsole.Markup("[gray][italic](" + expression + ")... [/][/]");

            //Evaluate it
            try
            {
                DataTable dt = new DataTable();
                object result = dt.Compute(expression, "");
                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
                return expression + " = " + Convert.ToDouble(result).ToString();
            }
            catch (Exception ex)
            {
                return "Error evaluating expression '" + expression + "': " + ex.Message;
            }
        }
    }
}
