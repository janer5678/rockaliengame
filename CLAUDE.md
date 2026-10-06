# Working on this repo

- **At the end of every prompt, commit and push to GitHub**: commit the finished work on `main` and `git push` it before replying, so the work is on GitHub as soon as the reply lands.
- Unity 6 (6000.0.42f1), URP, Netcode for GameObjects. Build headless with
  `"C:/Program Files/Unity/Hub/Editor/6000.0.42f1/Editor/Unity.exe" -batchmode -quit -projectPath . -executeMethod RockGame.EditorTools.ProjectSetup.BuildWindows -logFile Logs/batch_build.log`
  and check it with the `-autotest` modes described in README.md.
- The game's mechanics are settled. Only change the tutorial (Tutorial.cs) when a request asks for tutorial changes; don't rework it to match other mechanic changes.
- Don't spend effort on PSX graphics mode or rock (stone) nodes: neither is used in the game. Don't update or test them.
- End every reply that changes the game with a **super simple bulleted playtest list**: one short line per change, saying what to try in game, so it's clear what to playtest in each patch.
- Unity may be installed at `C:/Unity/6000.0.42f1/Editor/Unity.exe` instead of the Hub path above; use whichever exists. `Tools/compile_check.ps1` is a fast compile check that works while the editor is open.
