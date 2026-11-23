# Third-party notices

cmdr, logvu and cfgedit are published as single executables that include the third-party components below. Each
component is used under its own licence, reproduced or referenced here. Build-only tools (compilers, analyzers, source
link, GitVersion, the IL linker tasks) are not shipped and are not listed.

The versions are the ones the build resolves. When a dependency changes, update this file (`dotnet list src/<App> package
--include-transitive` lists them).

## Components

| Component | Version | Licence | Copyright | Project |
|---|---|---|---|---|
| Terminal.Gui (fork, built from the `Terminal.Gui` submodule) | 2.6.0 | MIT | Copyright 2007-2011 Novell Inc; Copyright 2017 Microsoft Corp | https://github.com/gui-cs/Terminal.Gui |
| Terminal.Gui.Editor | 2.5.7 | MIT | Copyright (c) gui.cs | https://github.com/tui-cs/Editor |
| ColorHelper | 1.8.1 | MIT | Artyom Gritsuk (author) | https://github.com/iamartyom/ColorHelper |
| JetBrains.Annotations | 2026.2.0 | MIT | Copyright (c) 2016-2025 JetBrains s.r.o. | https://www.jetbrains.com/help/resharper/Code_Analysis__Code_Annotations.html |
| Markdig | 1.4.2 | BSD-2-Clause | Copyright (c) Alexandre Mutel | https://github.com/xoofx/markdig |
| Microsoft.Extensions.Configuration, .Abstractions, .Binder, .FileExtensions, .Json | 10.0.13 | MIT | © Microsoft Corporation | https://github.com/dotnet/runtime |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.13 | MIT | © Microsoft Corporation | https://github.com/dotnet/runtime |
| Microsoft.Extensions.FileProviders.Abstractions, .Physical | 10.0.13 | MIT | © Microsoft Corporation | https://github.com/dotnet/runtime |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.13 | MIT | © Microsoft Corporation | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Abstractions | 10.0.7 | MIT | © Microsoft Corporation | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Options, .Primitives | 10.0.13 | MIT | © Microsoft Corporation | https://github.com/dotnet/runtime |
| Onigwrap (includes Oniguruma) | 1.0.11 | MIT; Oniguruma: BSD-2-Clause | Copyright (c) 2024 Aikawa Yataro; Oniguruma: Copyright (c) 2002-2021 K.Kosako | https://github.com/aikawayataro/Onigwrap |
| System.IO.Abstractions, TestableIO.System.IO.Abstractions, TestableIO.System.IO.Abstractions.Wrappers | 22.2.0 | MIT | Copyright © Tatham Oddie & friends 2010-2026 | https://github.com/TestableIO/System.IO.Abstractions |
| Testably.Abstractions.FileSystem.Interface | 10.3.0 | MIT | Copyright (c) 2024-2026 Testably | https://github.com/Testably/Testably.Abstractions |
| TextMateSharp, TextMateSharp.Grammars | 2.0.4 | MIT | Daniel Peñalba (author) | https://github.com/danipen/TextMateSharp |
| Tomlyn | 2.11.1 | BSD-2-Clause | Copyright (c) Alexandre Mutel | https://github.com/xoofx/Tomlyn |
| Wcwidth | 4.0.1 | MIT | Patrik Svensson and contributors (authors) | https://github.com/spectreconsole/wcwidth |
| YamlDotNet | 18.1.0 | MIT | Copyright (c) Antoine Aubry and contributors | https://github.com/aaubry/YamlDotNet |

Where the package declares no copyright line, the table names its authors as the package metadata gives them.

The Microsoft.Extensions packages and Onigwrap carry their own `THIRD-PARTY-NOTICES.TXT` for code they incorporate.
Those notices apply as published with the packages; for the Microsoft packages see
https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT. Oniguruma's licence, from Onigwrap's notices, is
reproduced below.

## MIT License

Applies to every component above marked MIT, with that component's copyright line.

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## BSD 2-Clause License

Applies to Markdig, Tomlyn and Oniguruma, with each component's copyright line.

```
Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following
   disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
   disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
