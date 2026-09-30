# Working on this repo

- **At the start of every prompt, push to GitHub first**: `git push` whatever is committed on `main` (the work from the previous prompt), before starting on the new request. Commit finished work at the end of each prompt so the next prompt's push picks it up.
- Unity 6 (6000.0.42f1), URP, Netcode for GameObjects. Build headless with
  `"C:/Program Files/Unity/Hub/Editor/6000.0.42f1/Editor/Unity.exe" -batchmode -quit -projectPath . -executeMethod RockGame.EditorTools.ProjectSetup.BuildWindows -logFile Logs/batch_build.log`
  and check it with the `-autotest` modes described in README.md.
