# Working on this repo

- **At the start of every prompt, push to GitHub first**: `git push` whatever is committed on `main` (the work from the previous prompt), before starting on the new request. Commit finished work at the end of each prompt so the next prompt's push picks it up.
- Unity 6 (6000.0.42f1), URP, Netcode for GameObjects. Build headless with
  `"C:/Program Files/Unity/Hub/Editor/6000.0.42f1/Editor/Unity.exe" -batchmode -quit -projectPath . -executeMethod RockGame.EditorTools.ProjectSetup.BuildWindows -logFile Logs/batch_build.log`
  and check it with the `-autotest` modes described in README.md.
- The game's mechanics are settled. Only change the tutorial (Tutorial.cs) when a request asks for tutorial changes; don't rework it to match other mechanic changes.
- Unity may be installed at `C:/Unity/6000.0.42f1/Editor/Unity.exe` instead of the Hub path above; use whichever exists. `Tools/compile_check.ps1` is a fast compile check that works while the editor is open.
