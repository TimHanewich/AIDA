# AIDA: AI Desktop Assistant
![AIDA banner](https://i.imgur.com/824qYfQ.png)

AIDA is a lightweight terminal-based AI coding agent that connects to your own model deployment and helps you 
inspect files, edit code, and work locally with transparent token-based usage.

Think of AIDA as an alternative to tools like **Claude Code**, **GitHub Copilot**, **OpenAI Codex**, and other coding agent experiences, but with a deliberately simple, local-first terminal interface and a toolset you can clearly see, control, and extend.

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

## Why use AIDA over other agent tools?
AIDA can be used as a simple, lightweight but effective alternative to enterprise AI Agents like Claude Code, Codex, GitHub Copilot and others.

- **Bring your own model** - connect AIDA to your own deployment through **Microsoft Foundry**.
- **Transparent token costs** - you pay for model tokens directly. No need to familiarize yourself with, or work through, a separate AI coding assistant provider's billing model.
- **Transparent tool access** - the tools AIDA has are explicit and inspectable, not hidden behind mystery abstractions.
- **Local workflow friendly** - it can inspect folders, read and edit files, fetch webpages, and optionally use shell commands.
- **Open source and easy to modify** - if you want to change how it behaves, you can just edit the code.

## How to run AIDA
AIDA is super lightweight and easy to run on Windows and Linux!

To install, download AIDA from [the changelog](./changelog.md). Place it anywhere you want on your computer. Add it to your **path** variable so you can call AIDA easily!

You'll then configure AIDA to connect to your model in **Microsoft Foundry**, choose your authentication method, and optionally enable tools like **Shell** or built-in **Web Search**.

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
