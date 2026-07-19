# IconPull - Icon Extractor for `.exe`, `.dll`, `.mun`, `.ico`, and more

[![License](https://img.shields.io/github/license/mirbyte/IconPull?color=FFEBA5)](https://raw.githubusercontent.com/mirbyte/IconPull/main/LICENSE)
![Size](https://img.shields.io/github/repo-size/mirbyte/IconPull?label=size&color=FFEBA5)
[![Download Count](https://img.shields.io/github/downloads/mirbyte/IconPull/total?color=FFEBA5)](https://github.com/mirbyte/IconPull/releases/latest)
[![Latest Release](https://img.shields.io/github/release/mirbyte/IconPull.svg?color=FFEBA5)](https://github.com/mirbyte/IconPull/releases/latest)

IconPull is a small Windows desktop utility for extracting application and system icons from Windows resource files. It exports both the icon as embedded and as rendered by the Windows Shell, which helps when older tools cannot read modern Windows 11 resources.

## Why IconPull?

I wasn't satisfied with the existing options: some felt outdated, while others were more complicated than necessary for a focused extraction task. IconPull is my attempt to address both problems with a simpler workflow and support for newer Windows resource layouts, including `.mun` files used by Windows 11.

Many Windows 11 system resources are stored in `.mun` files rather than being available directly in `shell32.dll` or `imageres.dll`. When legacy tools show empty shell32.dll files in Windows 11, IconPull helps you locate icons in `C:\Windows\SystemResources` and extract them. It reads the selected source file without modifying Windows system files.

## Features

- Extract icons from `.exe`, `.dll`, `.mun`, `.ico`, `.cpl`, `.scr`, `.ocx`, and `.icl` files
- Export a Shell-rendered PNG, the raw embedded `.ico`, or the best embedded icon frame as a `.png` with source transparency
- Choose Shell-rendered output sizes from 64 px to 1024 px and optionally allow Shell upscaling
- Drag and drop files or use the file picker
- Small Windows desktop utility with a self-contained publish option

The current release processes one source file at a time. Batch extraction and deep directory scanning are not yet supported. `.mun` handling is available now and will be expanded in future updates.

## Requirements

- Windows 10 or Windows 11
- .NET 10 Desktop Runtime, unless using a self-contained build

## Build

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run:

```powershell
dotnet build -c Release
```

To create a self-contained Windows x64 build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## FAQ

### How do I extract icons from Windows 11 .mun files?

Drop a `.mun` file into IconPull, choose the output folder and export formats, then click **Extract icons**. IconPull reads Windows resources through the Windows API. Support for additional `.mun` layouts will be expanded in future releases.

### Where are the default Windows 11 icons stored?

Many default Windows 11 icons are stored as `.mun` resources under `C:\Windows\SystemResources`. Select the relevant file in IconPull to preview and extract its available icons.

### How do I save a .exe icon as a transparent PNG?

Select or drop the `.exe`, enable **Raw best PNG** or **Shell-rendered PNG**, choose an output folder, and click **Extract icons**. The PNG export preserves transparency when it is present in the source icon.

<br>
<br>

<img width="1601" height="833" alt="ui" src="https://github.com/user-attachments/assets/f4d4df2a-eea2-429a-81a5-116b9401705c" />
