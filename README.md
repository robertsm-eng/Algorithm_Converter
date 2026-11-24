# VB.NET Algorithm Converter – Web edition

A browser-based teaching tool for A Level Computer Science. The VB.NET conversion engine and Roslyn syntax parser from the desktop edition are retained, with a Blazor WebAssembly interface. The supplied code is parsed, never executed. No server, login, database, API key, student installation or upload of student code is needed. The application files must first load from the website; this is not an installable offline PWA. GitHub may log ordinary website requests; the app itself sends no student code to any service.

## Host on GitHub Pages (one-time teacher setup)

1. Extract the ZIP. Open the extracted `algorithm-converter` folder. It contains `Core`, `Tests`, `Web`, `BrowserTests`, `.github`, `.gitignore`, and this README.
2. Go to https://github.com/new and create a **public** repository named `algorithm-converter`. Select **Add a README file** so the main branch exists. Public repositories support GitHub Pages on GitHub Free. Do not add student work, secrets, or internal college material to the repository.
3. On the repository's main page select **Add file → Upload files**. Drag the *contents* of the extracted folder (not the containing folder) into the upload area, including `.github`. Commit to `main`. Alternatively clone the repository with GitHub Desktop, copy the files into the clone, commit and push.
4. Verify `Web/Web.csproj`, `Core/Core.vbproj`, `Tests/Tests.vbproj`, `BrowserTests/smoke.cjs`, and `.github/workflows/pages.yml` are visible in the repository. If the workflow folder did not upload, select **Add file → Create new file**, type `.github/workflows/pages.yml` as the filename, and paste the contents of the local file of the same name. Commit to `main`.
5. Select **Settings → Pages → Build and deployment → Source → GitHub Actions**. Do not select a different pre-made workflow. If the initial workflow ran before Pages was enabled, that run may fail; the next step starts a fresh one.
6. Select the **Actions** tab, choose **Build and deploy converter**, then **Run workflow → main → Run workflow**. If GitHub asks you to enable Actions, do that first. The workflow installs .NET 10 on GitHub's runner, runs the original VB.NET tests, publishes the site, starts a local HTTP server and runs a Chromium browser smoke test. Only if all steps pass does it deploy to Pages.
7. Once the workflow is green, open **Settings → Pages → Visit site**. Your URL should be `https://YOUR-USERNAME.github.io/algorithm-converter/`. Test it on a student machine before sharing through Teams. Students need only the link and a modern browser with WebAssembly enabled. College IT may need to allow github.io and downloads of XML files.

Updates: commit/push changed files to main, and the workflow rebuilds and redeploys. No manual publish script required. GitHub Pages is public, including the app source on the free public-repository plan. The workflow computes the base URL from the Pages configuration, so a different repository name is also possible.

## Use the app

Paste VB.NET code or open a .vb file, then select Convert code. Choose a routine if the input contains more than one. Edit the pseudocode freely, copy it, or save as text. The preview can be zoomed and saved as SVG. Export Draw.io XML creates native editable diagram shapes and connectors and a second page containing the current pseudocode. Open it in https://app.diagrams.net via File → Open From → Device. Pseudocode changes do not update the flowchart; the diagram is generated from VB.NET. If VB.NET changes, convert again before exporting. No work is automatically saved; download or copy before closing the tab.

## Local development (optional, teacher only)

Install the .NET 10 SDK, then from the repository root run:

```
dotnet run --project Tests/Tests.vbproj -c Release
dotnet run --project Web/Web.csproj
```

Open the localhost address printed by dotnet. No student needs the SDK. The Core and Tests projects are VB.NET; Web uses C# and Razor for the browser UI because Blazor's standard tooling is C#-based. The converter itself is still VB.NET.

## Scope and verification

Supports sequence, assignment, input/output, If/ElseIf/Else, For/For Each, While, Do/Loop, Select Case, Return, Exit and Continue. Calls and expressions are retained rather than executed or recursively expanded. Syntax parsing does not type-check or semantically validate the code. Unsupported blocks are flagged; layouts may need adjustment for complex examples. Pseudocode is AQA-inspired, not an official AQA translator. Review all output before using it in assessed work.

The web edition has been source-reviewed but could not be compiled or browser-tested in the delivery environment (no .NET SDK and no network access for package restore). GitHub Actions performs those tests before deployment, including conversion, XML validity, edited pseudocode, stale-source export prevention and syntax errors. A green deployment is the first actual build verification. If a step fails, open that step in Actions and inspect its log.
