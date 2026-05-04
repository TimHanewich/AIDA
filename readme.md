# AIDA: AI Desktop Assistant
![AIDA banner](https://i.imgur.com/824qYfQ.png)

**AIDA**, short for **AI** **D**esktop **A**ssistant is a lightweight, console-based client for interfacing with a large language model.

AIDA has built-in tools (function-calling) that give it several capabilities:
- **Read File** - read the contents of a file of any type (txt, pdf, word document, etc.) from the local computer.
- **Write File** - create a new file on the local computer in the current directory.
- **Edit File** - edit an existing file by finding and replacing a specific string.
- **Delete File** - delete a file from the local computer.
- **Explore Directory** - list all files and sub-directories in a given directory.
- **Create Directory** - create a new directory (folder) on the local computer.
- **Delete Directory** - delete an empty directory from the local computer.
- **Check Current Time** - check the current date and time.
- **Read Webpage** - make an HTTP GET call to retrieve the contents of a URL endpoint (webpage or document).
- **View Image** - view an image from a local path or URL.
- **Shell** - execute a shell command on the host machine (enabled via settings).

In addition to the above capabilities, you can run several commands to manage the interfacing with the LLM (run command `/help` to list these): 
- `/clear` - clear the chat history.
- `/tools` - list the tools (function calling) available to AIDA.
- `/settings` - open AIDA's settings menu.
- `/auth` - authenticate into Foundry if using Service Principal.
- `/stats` - view usage statistics.

## How to run AIDA
AIDA is super lightweight and easy to run on Windows and Linux!

To install, download AIDA from [the changelog](./changelog.md). Place it anywhere you want on your computer. Add it to your **path** variable so you can call AIDA easily!

## Building the Project
If you modify AIDA and want to build it for yourself, here are the dotnet SDK commands you can use!

For windows:
```
dotnet publish AIDA.csproj -c Release --self-contained true
```

For linux:
```
dotnet publish AIDA.csproj -c Release -r linux-x64 --self-contained true
```

## Other Resources
- [AIDA Banner](https://github.com/TimHanewich/AIDA/releases/download/12/AIDA_header.pptx)
