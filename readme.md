# AIDA: AI Desktop Assistant
**AIDA**, short for **AI** **D**esktop **A**ssistant is a lightweight, console-based client for interfacing with a large language model.

AIDA has built-in tools (function-calling) that give it several capabilities:
- **Save File** - save a .txt file to the local computer.
- **Open Folder** - read the contents of a folder (files and sub-folders within it)
- **Read File** - read the contents of a file on the local computer.
- **Read Webpage** - read the content on a webpage (by a specific URL)
- **Check Weather** - check the weather for any location in the world.

In addition to the above capabilities, you can run several commands to manage the interfacing with the LLM (run command `help` to list these): 
- `tokens` - check the token consumption (input/output) for the current chat session
- `clear` - clear the chat history
- `tools` - list the tools (function calling) available to AIDA
- `save` - save the current chat history to a local file
- `load` - load chat history from a local file
- `settings` - update endpoint settings, color and theme, etc.

## How to run AIDA
1. Install the [.NET 9.0 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
2. Download AIDA from [the changelog](./changelog.md). Place it anywhere you want on your computer. Add it to your **path** variable so you can call AIDA easily!

## Building the Project
If you modify AIDA and want to build it for yourself, here are the dotnet SDK commands you can use!

For windows:
```
dotnet publish AIDA.csproj -c Release --self-contained false
```

For linux:
```
dotnet publish AIDA.csproj -c Release -r linux-x64 --self-contained false
```