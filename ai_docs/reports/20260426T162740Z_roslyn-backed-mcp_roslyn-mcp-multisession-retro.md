---
generated_at: "2026-04-26T16:37:00.5843374Z"
window: "last 14 days (2026-04-12T16:27:40Z to 2026-04-26T16:27:40Z)"
host_repo: roslyn-backed-mcp
host_repo_path: "C:\Code-Repo\Roslyn-Backed-MCP"
sessions_scanned: 248
sessions_included: 198
repos_covered:
  - cli-inventory-tool
  - code-repo
  - dotnet-firewall-analyzer
  - dotnet-network-documentation
  - it-chat-bot
  - jedi-py-mcp
  - jellyfin
  - roslyn-backed-mcp
  - syslog-server
  - tradewise
  - windows-system32
phase_mix:
  refactoring: 42
  release_operational: 21
  planning_docs: 37
  mixed: 98
truncated: false
---

# Roslyn MCP multi-session retrospective - 2026-04-26 - 14-day window

## 1. Session classification

Aggregate mix: 198 included sessions: 42 refactoring / 21 release-operational / 37 planning-docs / 98 mixed.

| session_id (short) | repo | date | phase | notes |
|---|---|---|---|---|
| 5111f317 | dotnet-firewall-analyzer | 2026-04-13 | mixed | 3 code edits; 4 docs/config edits; 6 workflow cmds; actual Roslyn calls |
| 7e2befc9 | dotnet-firewall-analyzer | 2026-04-13 | mixed | 7 docs/config edits; 10 workflow cmds; actual Roslyn calls |
| cb24d997 | dotnet-firewall-analyzer | 2026-04-13 | planning/docs | text-only Roslyn mention |
| cf2d0b85 | dotnet-firewall-analyzer | 2026-04-13 | refactoring | 57 code edits; 23 docs/config edits; 87 workflow cmds; actual Roslyn calls |
| 396e0724 | dotnet-network-documentation | 2026-04-13 | mixed | 3 docs/config edits; 55 workflow cmds; text-only Roslyn mention |
| 841d7d72 | dotnet-network-documentation | 2026-04-13 | planning/docs | text-only Roslyn mention |
| b5725fc4 | dotnet-network-documentation | 2026-04-13 | refactoring | 28 code edits; 8 docs/config edits; 22 workflow cmds; actual Roslyn calls |
| ce743c98 | dotnet-network-documentation | 2026-04-13 | release/operational | 6 workflow cmds; text-only Roslyn mention |
| d06f2c03 | dotnet-network-documentation | 2026-04-13 | refactoring | 67 code edits; 12 docs/config edits; 74 workflow cmds; text-only Roslyn mention |
| 2f8d073f | it-chat-bot | 2026-04-13 | release/operational | 1 workflow cmds; text-only Roslyn mention |
| 34850db5 | it-chat-bot | 2026-04-13 | mixed | 6 docs/config edits; 11 workflow cmds; text-only Roslyn mention |
| 385e725e | it-chat-bot | 2026-04-13 | refactoring | 59 code edits; 43 docs/config edits; 96 workflow cmds; actual Roslyn calls |
| c81b8c28 | it-chat-bot | 2026-04-13 | release/operational | 12 workflow cmds; text-only Roslyn mention |
| d02a79af | it-chat-bot | 2026-04-13 | planning/docs | text-only Roslyn mention |
| 0a75e638 | roslyn-backed-mcp | 2026-04-13 | refactoring | 63 code edits; 8 docs/config edits; 41 workflow cmds; text-only Roslyn mention |
| 10a651a8 | roslyn-backed-mcp | 2026-04-13 | refactoring | 6 code edits; 7 docs/config edits; 19 workflow cmds; text-only Roslyn mention |
| 1d93dd85 | roslyn-backed-mcp | 2026-04-13 | mixed | 4 code edits; 18 docs/config edits; 65 workflow cmds; actual Roslyn calls |
| 1fc8a6a6 | roslyn-backed-mcp | 2026-04-13 | refactoring | 5 code edits; 1 docs/config edits; 5 workflow cmds; actual Roslyn calls |
| 30778f45 | roslyn-backed-mcp | 2026-04-13 | release/operational | 4 workflow cmds; text-only Roslyn mention |
| 417e78c1 | roslyn-backed-mcp | 2026-04-13 | planning/docs | 2 docs/config edits; text-only Roslyn mention |
| 57a0d696 | roslyn-backed-mcp | 2026-04-13 | refactoring | 70 code edits; 83 docs/config edits; 74 workflow cmds; actual Roslyn calls |
| 848388ed | roslyn-backed-mcp | 2026-04-13 | release/operational | 2 workflow cmds; text-only Roslyn mention |
| 8fd59d5c | roslyn-backed-mcp | 2026-04-13 | release/operational | 3 workflow cmds; actual Roslyn calls |
| 9a6d20dd | roslyn-backed-mcp | 2026-04-13 | planning/docs | text-only Roslyn mention |
| be347a77 | roslyn-backed-mcp | 2026-04-13 | planning/docs | text-only Roslyn mention |
| d8769094 | roslyn-backed-mcp | 2026-04-13 | release/operational | 1 workflow cmds; text-only Roslyn mention |
| e2440c2b | roslyn-backed-mcp | 2026-04-13 | release/operational | 1 workflow cmds; text-only Roslyn mention |
| 85c0808f | dotnet-firewall-analyzer | 2026-04-14 | refactoring | 86 code edits; 4 docs/config edits; 55 workflow cmds; text-only Roslyn mention |
| e5d6c9e1 | dotnet-firewall-analyzer | 2026-04-14 | planning/docs | text-only Roslyn mention |
| 2f0c9cb4 | dotnet-network-documentation | 2026-04-14 | planning/docs | text-only Roslyn mention |
| ba82c4a9 | dotnet-network-documentation | 2026-04-14 | refactoring | 26 code edits; 7 docs/config edits; 81 workflow cmds; text-only Roslyn mention |
| 2af99454 | jellyfin | 2026-04-14 | mixed | 54 docs/config edits; 21 workflow cmds; text-only Roslyn mention |
| 1873072a | roslyn-backed-mcp | 2026-04-14 | refactoring | 66 code edits; 23 docs/config edits; 22 workflow cmds; text-only Roslyn mention |
| 78b9b0ed | roslyn-backed-mcp | 2026-04-14 | planning/docs | text-only Roslyn mention |
| 99695696 | roslyn-backed-mcp | 2026-04-14 | refactoring | 165 code edits; 109 docs/config edits; 156 workflow cmds; actual Roslyn calls |
| bced4fe7 | roslyn-backed-mcp | 2026-04-14 | mixed | 26 docs/config edits; 43 workflow cmds; actual Roslyn calls |
| 10f34863 | dotnet-firewall-analyzer | 2026-04-15 | mixed | 23 docs/config edits; 31 workflow cmds; text-only Roslyn mention |
| 34ca7601 | dotnet-firewall-analyzer | 2026-04-15 | refactoring | 7 code edits; 6 docs/config edits; 14 workflow cmds; actual Roslyn calls |
| f50761ce | dotnet-firewall-analyzer | 2026-04-15 | refactoring | 12 code edits; 17 docs/config edits; 16 workflow cmds; actual Roslyn calls |
| 0736a269 | dotnet-network-documentation | 2026-04-15 | planning/docs | actual Roslyn calls |
| 4b2a15a7 | dotnet-network-documentation | 2026-04-15 | mixed | 1 code edits; 24 docs/config edits; 42 workflow cmds; text-only Roslyn mention |
| ac8b4d93 | dotnet-network-documentation | 2026-04-15 | refactoring | 25 code edits; 17 docs/config edits; 13 workflow cmds; actual Roslyn calls |
| bbc10ec7 | dotnet-network-documentation | 2026-04-15 | mixed | 3 docs/config edits; 15 workflow cmds; actual Roslyn calls |
| 121407c8 | it-chat-bot | 2026-04-15 | mixed | 1 code edits; 23 docs/config edits; 39 workflow cmds; text-only Roslyn mention |
| 2bee29e3 | it-chat-bot | 2026-04-15 | mixed | 3 docs/config edits; 12 workflow cmds; actual Roslyn calls |
| 91990003 | it-chat-bot | 2026-04-15 | mixed | 8 code edits; 23 docs/config edits; 11 workflow cmds; actual Roslyn calls |
| 265bbad3 | jellyfin | 2026-04-15 | release/operational | 1 workflow cmds; text-only Roslyn mention |
| 35b1e783 | jellyfin | 2026-04-15 | mixed | 9 docs/config edits; 8 workflow cmds; text-only Roslyn mention |
| 908c42e0 | jellyfin | 2026-04-15 | mixed | 1 docs/config edits; 4 workflow cmds; text-only Roslyn mention |
| a26e68cc | jellyfin | 2026-04-15 | planning/docs | text-only Roslyn mention |
| 0e22e70c | roslyn-backed-mcp | 2026-04-15 | refactoring | 114 code edits; 41 docs/config edits; 83 workflow cmds; actual Roslyn calls |
| 340883da | roslyn-backed-mcp | 2026-04-15 | release/operational | 4 workflow cmds; actual Roslyn calls |
| 63ab8ded | roslyn-backed-mcp | 2026-04-15 | mixed | 7 code edits; 32 docs/config edits; 63 workflow cmds; text-only Roslyn mention |
| aef3fb58 | roslyn-backed-mcp | 2026-04-15 | mixed | 1 code edits; 9 docs/config edits; 6 workflow cmds; actual Roslyn calls |
| bfe7443f | roslyn-backed-mcp | 2026-04-15 | refactoring | 18 code edits; 28 docs/config edits; 35 workflow cmds; actual Roslyn calls |
| e1869107 | roslyn-backed-mcp | 2026-04-15 | planning/docs | 32 docs/config edits; 2 workflow cmds; actual Roslyn calls |
| 8a081c98 | syslog-server | 2026-04-15 | mixed | 6 docs/config edits; 28 workflow cmds; text-only Roslyn mention |
| 45e883dc | jellyfin | 2026-04-16 | mixed | 13 docs/config edits; 10 workflow cmds; text-only Roslyn mention |
| 1bb76b7c | roslyn-backed-mcp | 2026-04-16 | planning/docs | text-only Roslyn mention |
| 2d5d4b02 | roslyn-backed-mcp | 2026-04-16 | mixed | 1 code edits; 19 docs/config edits; 26 workflow cmds; text-only Roslyn mention |
| aa40e67c | roslyn-backed-mcp | 2026-04-16 | planning/docs | text-only Roslyn mention |
| b093b4b1 | roslyn-backed-mcp | 2026-04-16 | refactoring | 42 code edits; 8 docs/config edits; 31 workflow cmds; actual Roslyn calls |
| b33047b0 | roslyn-backed-mcp | 2026-04-16 | mixed | 4 code edits; 35 workflow cmds; actual Roslyn calls |
| b3e4cb62 | roslyn-backed-mcp | 2026-04-16 | refactoring | 96 code edits; 101 docs/config edits; 184 workflow cmds; text-only Roslyn mention |
| ce5facd3 | roslyn-backed-mcp | 2026-04-16 | mixed | 21 docs/config edits; 4 workflow cmds; text-only Roslyn mention |
| d6134922 | roslyn-backed-mcp | 2026-04-16 | refactoring | 113 code edits; 28 docs/config edits; 72 workflow cmds; text-only Roslyn mention |
| d66595f1 | roslyn-backed-mcp | 2026-04-16 | mixed | 57 code edits; 122 docs/config edits; 135 workflow cmds; actual Roslyn calls |
| fb2dc342 | roslyn-backed-mcp | 2026-04-16 | mixed | 5 code edits; 23 docs/config edits; 64 workflow cmds; text-only Roslyn mention |
| 0568b042 | roslyn-backed-mcp | 2026-04-17 | planning/docs | actual Roslyn calls |
| 1e999fa2 | roslyn-backed-mcp | 2026-04-17 | mixed | 1 code edits; 8 docs/config edits; 16 workflow cmds; text-only Roslyn mention |
| 4deb84a5 | roslyn-backed-mcp | 2026-04-17 | mixed | 1 code edits; 24 docs/config edits; 38 workflow cmds; actual Roslyn calls |
| 7f26262c | roslyn-backed-mcp | 2026-04-17 | release/operational | 4 workflow cmds; actual Roslyn calls |
| a94ece8f | roslyn-backed-mcp | 2026-04-17 | mixed | 21 docs/config edits; 104 workflow cmds; text-only Roslyn mention |
| d582cbc4 | roslyn-backed-mcp | 2026-04-17 | mixed | 55 docs/config edits; 136 workflow cmds; text-only Roslyn mention |
| 3f08f256 | roslyn-backed-mcp | 2026-04-18 | mixed | 5 docs/config edits; 54 workflow cmds; actual Roslyn calls |
| 44d61464 | roslyn-backed-mcp | 2026-04-18 | mixed | 27 docs/config edits; 51 workflow cmds; actual Roslyn calls |
| 49e906be | roslyn-backed-mcp | 2026-04-18 | planning/docs | 30 docs/config edits; 2 workflow cmds; text-only Roslyn mention |
| 70711728 | roslyn-backed-mcp | 2026-04-18 | planning/docs | text-only Roslyn mention |
| 74e889fd | roslyn-backed-mcp | 2026-04-18 | mixed | 1 code edits; 3 docs/config edits; 48 workflow cmds; text-only Roslyn mention |
| a2580a22 | roslyn-backed-mcp | 2026-04-18 | mixed | 2 code edits; 38 docs/config edits; 63 workflow cmds; actual Roslyn calls |
| a2fb805e | roslyn-backed-mcp | 2026-04-18 | mixed | 27 docs/config edits; 31 workflow cmds; text-only Roslyn mention |
| b5464409 | roslyn-backed-mcp | 2026-04-18 | mixed | 1 code edits; 44 docs/config edits; 38 workflow cmds; text-only Roslyn mention |
| c146e24d | roslyn-backed-mcp | 2026-04-18 | planning/docs | 10 docs/config edits; text-only Roslyn mention |
| 18e1aa86 | roslyn-backed-mcp | 2026-04-19 | mixed | 18 docs/config edits; 24 workflow cmds; actual Roslyn calls |
| 6c362c08 | roslyn-backed-mcp | 2026-04-19 | refactoring | 33 code edits; 6 docs/config edits; 26 workflow cmds; actual Roslyn calls |
| 80fa67a5 | roslyn-backed-mcp | 2026-04-19 | mixed | 9 docs/config edits; 15 workflow cmds; text-only Roslyn mention |
| a5c94ecd | roslyn-backed-mcp | 2026-04-19 | mixed | 1 code edits; 10 docs/config edits; 33 workflow cmds; actual Roslyn calls |
| f8564a04 | roslyn-backed-mcp | 2026-04-19 | mixed | 15 docs/config edits; 18 workflow cmds; text-only Roslyn mention |
| afe79a7c | dotnet-firewall-analyzer | 2026-04-20 | planning/docs | actual Roslyn calls |
| f8c6722a | dotnet-firewall-analyzer | 2026-04-20 | planning/docs | text-only Roslyn mention |
| 2e4a4c76 | it-chat-bot | 2026-04-20 | release/operational | 1 workflow cmds; actual Roslyn calls |
| 863b6cef | it-chat-bot | 2026-04-20 | planning/docs | text-only Roslyn mention |
| 0f0cc072 | roslyn-backed-mcp | 2026-04-20 | mixed | 1 code edits; 22 docs/config edits; 44 workflow cmds; text-only Roslyn mention |
| 291f0219 | roslyn-backed-mcp | 2026-04-20 | planning/docs | text-only Roslyn mention |
| 84fe8b1d | roslyn-backed-mcp | 2026-04-20 | refactoring | 7 code edits; 11 docs/config edits; 83 workflow cmds; text-only Roslyn mention |
| b59de024 | roslyn-backed-mcp | 2026-04-20 | mixed | 14 docs/config edits; 28 workflow cmds; text-only Roslyn mention |
| eac84b56 | roslyn-backed-mcp | 2026-04-20 | planning/docs | text-only Roslyn mention |
| ee2eb2ce | roslyn-backed-mcp | 2026-04-20 | mixed | 14 docs/config edits; 16 workflow cmds; text-only Roslyn mention |
| fb2af82f | roslyn-backed-mcp | 2026-04-20 | mixed | 2 docs/config edits; 3 workflow cmds; text-only Roslyn mention |
| 18b9aa97 | dotnet-network-documentation | 2026-04-21 | refactoring | 19 code edits; 2 docs/config edits; 21 workflow cmds; actual Roslyn calls |
| 29a28a96 | dotnet-network-documentation | 2026-04-21 | refactoring | 13 code edits; 4 docs/config edits; 48 workflow cmds; text-only Roslyn mention |
| 60ab4ec6 | dotnet-network-documentation | 2026-04-21 | mixed | 24 docs/config edits; 41 workflow cmds; text-only Roslyn mention |
| 786f10b0 | dotnet-network-documentation | 2026-04-21 | release/operational | 1 workflow cmds; text-only Roslyn mention |
| e2c3fe7c | it-chat-bot | 2026-04-21 | refactoring | 7 code edits; 7 docs/config edits; 26 workflow cmds; actual Roslyn calls |
| eac7f094 | roslyn-backed-mcp | 2026-04-21 | mixed | 3 code edits; 7 docs/config edits; 71 workflow cmds; actual Roslyn calls |
| 8dd2411f | tradewise | 2026-04-21 | mixed | 6 docs/config edits; 20 workflow cmds; text-only Roslyn mention |
| dcef8d5b | tradewise | 2026-04-21 | mixed | 5 docs/config edits; 4 workflow cmds; text-only Roslyn mention |
| 137f2629 | dotnet-firewall-analyzer | 2026-04-22 | mixed | 5 docs/config edits; 21 workflow cmds; text-only Roslyn mention |
| 199aee46 | dotnet-network-documentation | 2026-04-22 | refactoring | 23 code edits; 10 docs/config edits; 40 workflow cmds; actual Roslyn calls |
| 2052c101 | dotnet-network-documentation | 2026-04-22 | mixed | 1 code edits; 49 workflow cmds; text-only Roslyn mention |
| 6909a4f5 | dotnet-network-documentation | 2026-04-22 | mixed | 2 code edits; 11 docs/config edits; 61 workflow cmds; actual Roslyn calls |
| e1444fcf | dotnet-network-documentation | 2026-04-22 | mixed | 1 docs/config edits; 39 workflow cmds; text-only Roslyn mention |
| e2c57ca5 | dotnet-network-documentation | 2026-04-22 | mixed | 63 docs/config edits; 21 workflow cmds; text-only Roslyn mention |
| f71cbc02 | dotnet-network-documentation | 2026-04-22 | refactoring | 11 code edits; 22 docs/config edits; 89 workflow cmds; actual Roslyn calls |
| 01c5f16b | it-chat-bot | 2026-04-22 | mixed | 6 docs/config edits; 41 workflow cmds; text-only Roslyn mention |
| c60ae8a3 | jellyfin | 2026-04-22 | mixed | 26 docs/config edits; 20 workflow cmds; text-only Roslyn mention |
| 08adc1f1 | roslyn-backed-mcp | 2026-04-22 | refactoring | 13 code edits; 17 docs/config edits; 39 workflow cmds; actual Roslyn calls |
| 14f08c77 | roslyn-backed-mcp | 2026-04-22 | mixed | 21 docs/config edits; 31 workflow cmds; text-only Roslyn mention |
| 1c540bfa | roslyn-backed-mcp | 2026-04-22 | mixed | 3 code edits; 6 docs/config edits; 36 workflow cmds; actual Roslyn calls |
| 68ae4b6a | roslyn-backed-mcp | 2026-04-22 | mixed | 1 code edits; 7 docs/config edits; 39 workflow cmds; actual Roslyn calls |
| dfc7938d | roslyn-backed-mcp | 2026-04-22 | mixed | 5 docs/config edits; 20 workflow cmds; text-only Roslyn mention |
| f88bc978 | roslyn-backed-mcp | 2026-04-22 | release/operational | 1 workflow cmds; actual Roslyn calls |
| 5bb1aa25 | syslog-server | 2026-04-22 | mixed | 6 docs/config edits; 4 workflow cmds; text-only Roslyn mention |
| 5ed94ddd | dotnet-network-documentation | 2026-04-23 | refactoring | 45 code edits; 22 docs/config edits; 137 workflow cmds; actual Roslyn calls |
| 9c5e52fa | it-chat-bot | 2026-04-23 | planning/docs | actual Roslyn calls |
| 8ab17899 | jedi-py-mcp | 2026-04-23 | release/operational | 21 workflow cmds; text-only Roslyn mention |
| e22f5fb3 | cli-inventory-tool | 2026-04-24 | mixed | 6 docs/config edits; 7 workflow cmds; text-only Roslyn mention |
| be1a26f1 | code-repo | 2026-04-24 | planning/docs | text-only Roslyn mention |
| 24d115bf | dotnet-firewall-analyzer | 2026-04-24 | mixed | 5 docs/config edits; 16 workflow cmds; text-only Roslyn mention |
| 60d52f64 | dotnet-firewall-analyzer | 2026-04-24 | mixed | 1 docs/config edits; 22 workflow cmds; text-only Roslyn mention |
| 93343cd5 | dotnet-firewall-analyzer | 2026-04-24 | mixed | 4 code edits; 13 docs/config edits; 26 workflow cmds; text-only Roslyn mention |
| d8763f40 | dotnet-firewall-analyzer | 2026-04-24 | mixed | 4 docs/config edits; 15 workflow cmds; actual Roslyn calls |
| fd70d28f | dotnet-firewall-analyzer | 2026-04-24 | release/operational | 15 workflow cmds; text-only Roslyn mention |
| 09ea14c5 | dotnet-network-documentation | 2026-04-24 | mixed | 18 docs/config edits; 69 workflow cmds; text-only Roslyn mention |
| 0c1aaf7c | dotnet-network-documentation | 2026-04-24 | refactoring | 13 code edits; 2 docs/config edits; 26 workflow cmds; actual Roslyn calls |
| 260c3f80 | dotnet-network-documentation | 2026-04-24 | refactoring | 16 code edits; 4 docs/config edits; 27 workflow cmds; text-only Roslyn mention |
| 4e707c75 | dotnet-network-documentation | 2026-04-24 | refactoring | 34 code edits; 12 docs/config edits; 22 workflow cmds; text-only Roslyn mention |
| 5687cbf9 | dotnet-network-documentation | 2026-04-24 | refactoring | 8 code edits; 3 docs/config edits; 47 workflow cmds; actual Roslyn calls |
| 5b7d0a31 | dotnet-network-documentation | 2026-04-24 | planning/docs | 12 docs/config edits; text-only Roslyn mention |
| dd0a7e48 | dotnet-network-documentation | 2026-04-24 | mixed | 7 docs/config edits; 9 workflow cmds; actual Roslyn calls |
| e6777561 | dotnet-network-documentation | 2026-04-24 | refactoring | 5 code edits; 7 docs/config edits; 22 workflow cmds; actual Roslyn calls |
| 0f6987a3 | it-chat-bot | 2026-04-24 | mixed | 2 docs/config edits; 14 workflow cmds; text-only Roslyn mention |
| 102bd77d | it-chat-bot | 2026-04-24 | refactoring | 10 code edits; 3 docs/config edits; 21 workflow cmds; actual Roslyn calls |
| 173f2cb3 | it-chat-bot | 2026-04-24 | planning/docs | text-only Roslyn mention |
| 35aebb81 | it-chat-bot | 2026-04-24 | planning/docs | 1 docs/config edits; text-only Roslyn mention |
| c7518401 | it-chat-bot | 2026-04-24 | mixed | 4 docs/config edits; 12 workflow cmds; actual Roslyn calls |
| cc335faa | it-chat-bot | 2026-04-24 | planning/docs | 1 docs/config edits; 2 workflow cmds; text-only Roslyn mention |
| e621ebec | it-chat-bot | 2026-04-24 | release/operational | 2 workflow cmds; actual Roslyn calls |
| fa338314 | it-chat-bot | 2026-04-24 | planning/docs | text-only Roslyn mention |
| 0ccdf69d | jedi-py-mcp | 2026-04-24 | mixed | 29 docs/config edits; 36 workflow cmds; text-only Roslyn mention |
| af216fda | jedi-py-mcp | 2026-04-24 | mixed | 8 docs/config edits; 23 workflow cmds; text-only Roslyn mention |
| 0fcd16ce | roslyn-backed-mcp | 2026-04-24 | planning/docs | text-only Roslyn mention |
| 14377637 | roslyn-backed-mcp | 2026-04-24 | planning/docs | text-only Roslyn mention |
| 28e53529 | roslyn-backed-mcp | 2026-04-24 | mixed | 12 docs/config edits; 10 workflow cmds; actual Roslyn calls |
| 2b78cc79 | roslyn-backed-mcp | 2026-04-24 | mixed | 1 code edits; 11 docs/config edits; 25 workflow cmds; actual Roslyn calls |
| 6ffeb393 | roslyn-backed-mcp | 2026-04-24 | mixed | 18 docs/config edits; 19 workflow cmds; text-only Roslyn mention |
| 79d56725 | roslyn-backed-mcp | 2026-04-24 | release/operational | 2 workflow cmds; text-only Roslyn mention |
| 81a4342d | roslyn-backed-mcp | 2026-04-24 | mixed | 17 docs/config edits; 12 workflow cmds; text-only Roslyn mention |
| 85d8349d | roslyn-backed-mcp | 2026-04-24 | planning/docs | 1 docs/config edits; text-only Roslyn mention |
| 8cedf628 | roslyn-backed-mcp | 2026-04-24 | mixed | 21 docs/config edits; 33 workflow cmds; actual Roslyn calls |
| a2f11d58 | roslyn-backed-mcp | 2026-04-24 | mixed | 17 docs/config edits; 23 workflow cmds; text-only Roslyn mention |
| b0790994 | roslyn-backed-mcp | 2026-04-24 | mixed | 67 docs/config edits; 40 workflow cmds; text-only Roslyn mention |
| b8d60dae | roslyn-backed-mcp | 2026-04-24 | planning/docs | text-only Roslyn mention |
| bf75bd99 | roslyn-backed-mcp | 2026-04-24 | mixed | 2 code edits; 16 docs/config edits; 43 workflow cmds; actual Roslyn calls |
| e5e9032d | roslyn-backed-mcp | 2026-04-24 | mixed | 3 code edits; 21 docs/config edits; 36 workflow cmds; actual Roslyn calls |
| ed384448 | roslyn-backed-mcp | 2026-04-24 | mixed | 17 docs/config edits; 21 workflow cmds; actual Roslyn calls |
| fb22f6ce | roslyn-backed-mcp | 2026-04-24 | mixed | 2 code edits; 22 docs/config edits; 32 workflow cmds; actual Roslyn calls |
| 080267e3 | syslog-server | 2026-04-24 | planning/docs | 1 docs/config edits; text-only Roslyn mention |
| 3937164b | syslog-server | 2026-04-24 | planning/docs | 1 docs/config edits; text-only Roslyn mention |
| 8e1d3c8a | syslog-server | 2026-04-24 | mixed | 14 docs/config edits; 40 workflow cmds; text-only Roslyn mention |
| 75155c69 | tradewise | 2026-04-24 | release/operational | 2 workflow cmds; text-only Roslyn mention |
| 8c1a4e3b | tradewise | 2026-04-24 | release/operational | 4 workflow cmds; text-only Roslyn mention |
| a663828b | tradewise | 2026-04-24 | mixed | 9 docs/config edits; 5 workflow cmds; text-only Roslyn mention |
| 4deec189 | windows-system32 | 2026-04-24 | mixed | 10 docs/config edits; 8 workflow cmds; text-only Roslyn mention |
| 3cb7aaff | dotnet-network-documentation | 2026-04-25 | refactoring | 21 code edits; 42 docs/config edits; 115 workflow cmds; text-only Roslyn mention |
| aa8f6e25 | dotnet-network-documentation | 2026-04-25 | mixed | 16 docs/config edits; 31 workflow cmds; text-only Roslyn mention |
| b773728c | it-chat-bot | 2026-04-25 | refactoring | 14 code edits; 15 docs/config edits; 42 workflow cmds; text-only Roslyn mention |
| 056cd648 | roslyn-backed-mcp | 2026-04-25 | mixed | 11 docs/config edits; 24 workflow cmds; text-only Roslyn mention |
| 28708b4c | roslyn-backed-mcp | 2026-04-25 | mixed | 25 docs/config edits; 60 workflow cmds; text-only Roslyn mention |
| 724b310b | roslyn-backed-mcp | 2026-04-25 | release/operational | 11 workflow cmds; text-only Roslyn mention |
| 8b2fdc28 | roslyn-backed-mcp | 2026-04-25 | mixed | 1 code edits; 4 docs/config edits; 25 workflow cmds; actual Roslyn calls |
| 92c94926 | roslyn-backed-mcp | 2026-04-25 | mixed | 22 docs/config edits; 32 workflow cmds; text-only Roslyn mention |
| 98b1d802 | roslyn-backed-mcp | 2026-04-25 | mixed | 3 docs/config edits; 6 workflow cmds; text-only Roslyn mention |
| f61023b3 | roslyn-backed-mcp | 2026-04-25 | mixed | 13 docs/config edits; 37 workflow cmds; text-only Roslyn mention |
| c4698457 | syslog-server | 2026-04-25 | mixed | 11 docs/config edits; 11 workflow cmds; text-only Roslyn mention |
| ade7b1c3 | dotnet-firewall-analyzer | 2026-04-26 | refactoring | 13 code edits; 17 docs/config edits; 54 workflow cmds; text-only Roslyn mention |
| c24725fb | dotnet-firewall-analyzer | 2026-04-26 | mixed | 2 docs/config edits; 29 workflow cmds; text-only Roslyn mention |
| 6e728f1c | dotnet-network-documentation | 2026-04-26 | refactoring | 12 code edits; 15 docs/config edits; 38 workflow cmds; actual Roslyn calls |
| b4821b06 | dotnet-network-documentation | 2026-04-26 | refactoring | 7 code edits; 4 docs/config edits; 17 workflow cmds; actual Roslyn calls |
| ed9b5cb5 | dotnet-network-documentation | 2026-04-26 | refactoring | 10 code edits; 20 docs/config edits; 52 workflow cmds; text-only Roslyn mention |
| 3857774d | it-chat-bot | 2026-04-26 | refactoring | 12 code edits; 6 docs/config edits; 30 workflow cmds; text-only Roslyn mention |
| 6d1a0a56 | jedi-py-mcp | 2026-04-26 | mixed | 16 docs/config edits; 14 workflow cmds; text-only Roslyn mention |
| 32f1234d | roslyn-backed-mcp | 2026-04-26 | planning/docs | actual Roslyn calls |
| 642d495c | roslyn-backed-mcp | 2026-04-26 | mixed | 17 docs/config edits; 35 workflow cmds; text-only Roslyn mention |
| 690a972d | roslyn-backed-mcp | 2026-04-26 | mixed | 9 docs/config edits; 42 workflow cmds; actual Roslyn calls |
| 73c9ad0b | roslyn-backed-mcp | 2026-04-26 | mixed | 2 code edits; 28 docs/config edits; 39 workflow cmds; text-only Roslyn mention |
| 73c918db | syslog-server | 2026-04-26 | mixed | 57 docs/config edits; 57 workflow cmds; text-only Roslyn mention |
| 089bd32b | tradewise | 2026-04-26 | mixed | 3 code edits; 22 docs/config edits; 62 workflow cmds; text-only Roslyn mention |

## 2. Task inventory (aggregated, with session ids)

| Session(s) | Task (one-line verb phrase) | Tool actually used | File type / domain | Right tool for the job? |
|---|---|---|---|---|
| 73c9ad0b, 74e889fd, 6c362c08, 84fe8b1d, 8b2fdc28, 99695696, 68ae4b6a, 1c540bfa, 1d93dd85, 1e999fa2, 1fc8a6a6, 2b78cc79, 2d5d4b02, 4deb84a5, 57a0d696, 63ab8ded, a2580a22, a5c94ecd, fb22f6ce, fb2dc342, 089bd32b, eac7f094, 1873072a, e5e9032d, aef3fb58, b093b4b1, b33047b0, b3e4cb62, b5464409, bf75bd99, bfe7443f, d6134922, d66595f1, 10a651a8, 2052c101, 260c3f80, 29a28a96, 3cb7aaff, 4b2a15a7, 4e707c75, 5687cbf9, 5ed94ddd, 6909a4f5, 6e728f1c, ac8b4d93, b4821b06, b5725fc4, ba82c4a9, 199aee46, 18b9aa97, 34ca7601, 5111f317, 85c0808f, 93343cd5, ade7b1c3, cf2d0b85, f50761ce, 0c1aaf7c, e2c3fe7c, 08adc1f1, 0a75e638, 0e22e70c, 0f0cc072, d06f2c03, e6777561, ed9b5cb5, f71cbc02, 102bd77d, 121407c8, 3857774d, 385e725e, 91990003, b773728c | Edit C# source, tests, project files (73x) | Edit / Write / MultiEdit, with occasional Roslyn preview/apply | C# / csproj / solution | Mostly right for pinpoint or bootstrap self-edit work; missed opportunity when symbol-safe Roslyn preview covered rename/move/extract/remove flows. |
| 6ffeb393, 73c9ad0b, 74e889fd, 80fa67a5, 6c362c08, 81a4342d, 84fe8b1d, 85d8349d, 8b2fdc28, 8cedf628, 92c94926, 98b1d802, 99695696, 690a972d, 68ae4b6a, 642d495c, 18e1aa86, 1c540bfa, 1d93dd85, 1e999fa2, 1fc8a6a6, 28708b4c, 28e53529, 2b78cc79, 2d5d4b02, 3f08f256, 417e78c1, 44d61464, 49e906be, 4deb84a5, 57a0d696, 63ab8ded, a2580a22, a2f11d58, a2fb805e, a5c94ecd, ed384448, ee2eb2ce, f61023b3, f8564a04, fb22f6ce, fb2af82f, fb2dc342, 080267e3, 3937164b, 5bb1aa25, 73c918db, 8a081c98, 8e1d3c8a, c4698457, 089bd32b, 8dd2411f, a663828b, eac7f094, 1873072a, e5e9032d, e1869107, a94ece8f, aef3fb58, b0790994, b093b4b1, b3e4cb62, b5464409, b59de024, bced4fe7, bf75bd99, bfe7443f, c146e24d, ce5facd3, d582cbc4, d6134922, d66595f1, dfc7938d, 14f08c77, 10a651a8, 260c3f80, 29a28a96, 396e0724, 3cb7aaff, 4b2a15a7, 4e707c75, 5687cbf9, 5b7d0a31, 5ed94ddd, 60ab4ec6, 6909a4f5, 6e728f1c, aa8f6e25, ac8b4d93, b4821b06, b5725fc4, ba82c4a9, 199aee46, bbc10ec7, 18b9aa97, 09ea14c5, e22f5fb3, 10f34863, 137f2629, 24d115bf, 34ca7601, 5111f317, 60d52f64, 7e2befc9, 85c0808f, 93343cd5, ade7b1c3, c24725fb, cf2d0b85, d8763f40, f50761ce, 0c1aaf7c, dcef8d5b, dd0a7e48, e2c3fe7c, 0ccdf69d, 6d1a0a56, af216fda, 2af99454, 35b1e783, 45e883dc, 908c42e0, c60ae8a3, 056cd648, 08adc1f1, 0a75e638, 0e22e70c, 0f0cc072, d06f2c03, cc335faa, c7518401, e1444fcf, e2c57ca5, e6777561, ed9b5cb5, f71cbc02, 01c5f16b, 0f6987a3, 102bd77d, 121407c8, 2bee29e3, 34850db5, 35aebb81, 3857774d, 385e725e, 91990003, b773728c, 4deec189 | Edit AI docs, backlog, plans, changelog fragments, reports (148x) | Edit / Write | Markdown / JSON / YAML / text | Yes. Textual editors are the right tool. |
| 73c9ad0b, 74e889fd, 6c362c08, 81a4342d, 84fe8b1d, 8b2fdc28, 8cedf628, 8fd59d5c, 99695696, 68ae4b6a, 642d495c, 18e1aa86, 1c540bfa, 1d93dd85, 1e999fa2, 1fc8a6a6, 28708b4c, 28e53529, 2b78cc79, 2d5d4b02, 30778f45, 340883da, 3f08f256, 44d61464, 49e906be, 4deb84a5, 57a0d696, 63ab8ded, a2580a22, a2fb805e, a5c94ecd, ed384448, ee2eb2ce, f61023b3, f88bc978, fb22f6ce, fb2dc342, 5bb1aa25, 8a081c98, 8e1d3c8a, c4698457, 089bd32b, a663828b, eac7f094, 1873072a, e5e9032d, e1869107, a94ece8f, aef3fb58, b0790994, b093b4b1, b33047b0, b3e4cb62, b5464409, b59de024, bced4fe7, bf75bd99, bfe7443f, d582cbc4, d6134922, d66595f1, dfc7938d, 14f08c77, 10a651a8, 2052c101, 260c3f80, 29a28a96, 396e0724, 3cb7aaff, 4b2a15a7, 4e707c75, 5687cbf9, 5ed94ddd, 60ab4ec6, 6909a4f5, 6e728f1c, 786f10b0, aa8f6e25, ac8b4d93, b4821b06, b5725fc4, ba82c4a9, 199aee46, bbc10ec7, 18b9aa97, 09ea14c5, e22f5fb3, 10f34863, 137f2629, 24d115bf, 34ca7601, 5111f317, 60d52f64, 7e2befc9, 85c0808f, 93343cd5, ade7b1c3, c24725fb, cf2d0b85, d8763f40, f50761ce, fd70d28f, 0c1aaf7c, dcef8d5b, ce743c98, dd0a7e48, e2c3fe7c, e621ebec, af216fda, 265bbad3, 2af99454, 35b1e783, 45e883dc, 908c42e0, c60ae8a3, 08adc1f1, 0a75e638, 0e22e70c, 0f0cc072, d06f2c03, cc335faa, c7518401, e1444fcf, e2c57ca5, e6777561, ed9b5cb5, f71cbc02, 01c5f16b, 102bd77d, 121407c8, 2bee29e3, 2e4a4c76, 2f8d073f, 34850db5, 3857774d, 385e725e, 91990003, b773728c, c81b8c28 | Run build, test, verify, restore, package commands (139x) | Bash dotnet / just / eng/*.ps1 | Build and validation | Yes for CI parity; missed opportunity when used as the inner post-edit loop instead of compile_check/test_related_files/test_run. |
| 6ffeb393, 724b310b, 73c9ad0b, 74e889fd, 79d56725, 7f26262c, 80fa67a5, 6c362c08, 81a4342d, 84fe8b1d, 8b2fdc28, 8cedf628, 92c94926, 98b1d802, 99695696, 690a972d, 68ae4b6a, 642d495c, 18e1aa86, 1c540bfa, 1d93dd85, 1e999fa2, 28708b4c, 28e53529, 2b78cc79, 2d5d4b02, 30778f45, 340883da, 3f08f256, 44d61464, 49e906be, 4deb84a5, 57a0d696, 63ab8ded, a2580a22, a2f11d58, a2fb805e, a5c94ecd, ed384448, ee2eb2ce, f61023b3, f8564a04, fb22f6ce, fb2af82f, fb2dc342, 5bb1aa25, 73c918db, 8a081c98, 8e1d3c8a, c4698457, 089bd32b, 75155c69, 8c1a4e3b, 8dd2411f, a663828b, eac7f094, e5e9032d, e1869107, a94ece8f, aef3fb58, b0790994, b093b4b1, b33047b0, b3e4cb62, b5464409, b59de024, bced4fe7, bf75bd99, bfe7443f, ce5facd3, d582cbc4, d6134922, d66595f1, dfc7938d, 14f08c77, 10a651a8, 2052c101, 260c3f80, 29a28a96, 396e0724, 3cb7aaff, 4b2a15a7, 5687cbf9, 5ed94ddd, 60ab4ec6, 6909a4f5, 6e728f1c, aa8f6e25, ac8b4d93, b4821b06, b5725fc4, ba82c4a9, 199aee46, bbc10ec7, 18b9aa97, 09ea14c5, e22f5fb3, 10f34863, 137f2629, 24d115bf, 34ca7601, 60d52f64, 7e2befc9, 85c0808f, 93343cd5, ade7b1c3, c24725fb, cf2d0b85, d8763f40, f50761ce, fd70d28f, 0c1aaf7c, dd0a7e48, e2c3fe7c, e621ebec, 0ccdf69d, 6d1a0a56, 8ab17899, af216fda, 2af99454, 35b1e783, 45e883dc, c60ae8a3, 056cd648, 08adc1f1, 0a75e638, 0e22e70c, 0f0cc072, d06f2c03, cc335faa, c7518401, e1444fcf, e2c57ca5, e6777561, ed9b5cb5, f71cbc02, 01c5f16b, 0f6987a3, 102bd77d, 121407c8, 2bee29e3, 34850db5, 3857774d, 385e725e, 91990003, b773728c, 4deec189 | Run branch, PR, merge, and reconciliation workflow (147x) | Bash git / gh | GitHub workflow | Yes. Native git/gh commands are the right surface. |
| 7f26262c, 6c362c08, 8b2fdc28, 8cedf628, 8fd59d5c, 99695696, 690a972d, 68ae4b6a, 18e1aa86, 1c540bfa, 1d93dd85, 1fc8a6a6, 28e53529, 2b78cc79, 32f1234d, 340883da, 3f08f256, 44d61464, 4deb84a5, 57a0d696, a2580a22, a5c94ecd, ed384448, f88bc978, fb22f6ce, eac7f094, e5e9032d, e1869107, aef3fb58, b093b4b1, b33047b0, bced4fe7, bf75bd99, bfe7443f, d66595f1, 5687cbf9, 5ed94ddd, 6909a4f5, 6e728f1c, ac8b4d93, b4821b06, b5725fc4, 199aee46, bbc10ec7, 18b9aa97, 34ca7601, 5111f317, 7e2befc9, afe79a7c, cf2d0b85, d8763f40, f50761ce, 0736a269, 0c1aaf7c, dd0a7e48, e2c3fe7c, e621ebec, 0568b042, 08adc1f1, 0e22e70c, c7518401, e6777561, f71cbc02, 102bd77d, 2bee29e3, 2e4a4c76, 385e725e, 91990003, 9c5e52fa | Use Roslyn MCP semantic read/refactor/validation tools (69x) | mcp__roslyn__* | C# semantic analysis/refactoring | Yes when available; failures and gaps are broken out in sections 2a and 2b. |
| 6ffeb393, 70711728, 724b310b, 73c9ad0b, 74e889fd, 7f26262c, 80fa67a5, 6c362c08, 81a4342d, 84fe8b1d, 92c94926, 98b1d802, 99695696, 690a972d, 68ae4b6a, 642d495c, 18e1aa86, 1c540bfa, 1d93dd85, 1e999fa2, 1fc8a6a6, 28708b4c, 28e53529, 291f0219, 2b78cc79, 2d5d4b02, 30778f45, 340883da, 3f08f256, 44d61464, 49e906be, 4deb84a5, 57a0d696, 63ab8ded, a2580a22, a2fb805e, a5c94ecd, ed384448, f61023b3, f8564a04, fb22f6ce, fb2af82f, 5bb1aa25, 73c918db, 8a081c98, 8e1d3c8a, 089bd32b, 75155c69, a663828b, eac7f094, 1873072a, e5e9032d, e1869107, a94ece8f, aa40e67c, aef3fb58, b0790994, b093b4b1, b33047b0, b3e4cb62, b5464409, b59de024, b8d60dae, bced4fe7, bf75bd99, bfe7443f, c146e24d, ce5facd3, d582cbc4, d6134922, d66595f1, d8769094, dfc7938d, 14f08c77, 14377637, 10a651a8, 2052c101, 260c3f80, 29a28a96, 2f0c9cb4, 396e0724, 3cb7aaff, 4b2a15a7, 4e707c75, 5687cbf9, 5ed94ddd, 60ab4ec6, 6909a4f5, 6e728f1c, aa8f6e25, ac8b4d93, b4821b06, b5725fc4, ba82c4a9, 199aee46, 18b9aa97, 09ea14c5, e22f5fb3, 10f34863, 137f2629, 24d115bf, 5111f317, 7e2befc9, 85c0808f, 93343cd5, ade7b1c3, afe79a7c, c24725fb, cf2d0b85, d8763f40, f50761ce, 0736a269, 0c1aaf7c, dcef8d5b, ce743c98, dd0a7e48, e2c3fe7c, e621ebec, 0ccdf69d, 6d1a0a56, 8ab17899, af216fda, 2af99454, 35b1e783, 45e883dc, a26e68cc, c60ae8a3, 0568b042, 056cd648, 08adc1f1, 0a75e638, 0f0cc072, d06f2c03, c7518401, e1444fcf, e2c57ca5, e6777561, ed9b5cb5, f71cbc02, 0f6987a3, 102bd77d, 121407c8, 2bee29e3, 2e4a4c76, 2f8d073f, 34850db5, 3857774d, 385e725e, 91990003, b773728c, c81b8c28 | Use Grep/Glob/Read for file or symbol discovery (151x) | Grep / Glob / Read | Mixed text and C# discovery | Right for arbitrary text/files; missed opportunity for caller/reference/type searches when a Roslyn workspace was loaded. |
| be1a26f1, 6ffeb393, 724b310b, 73c9ad0b, 74e889fd, 78b9b0ed, 79d56725, 7f26262c, 80fa67a5, 6c362c08, 81a4342d, 84fe8b1d, 8b2fdc28, 8cedf628, 8fd59d5c, 92c94926, 98b1d802, 99695696, 848388ed, 690a972d, 68ae4b6a, 642d495c, 18e1aa86, 1bb76b7c, 1c540bfa, 1d93dd85, 1e999fa2, 1fc8a6a6, 28708b4c, 28e53529, 291f0219, 2b78cc79, 2d5d4b02, 30778f45, 340883da, 3f08f256, 44d61464, 49e906be, 4deb84a5, 57a0d696, 63ab8ded, a2580a22, a2f11d58, a2fb805e, a5c94ecd, ed384448, ee2eb2ce, f61023b3, f8564a04, f88bc978, fb22f6ce, fb2af82f, fb2dc342, 3937164b, 5bb1aa25, 73c918db, 8a081c98, 8e1d3c8a, c4698457, 089bd32b, 75155c69, 8c1a4e3b, 8dd2411f, a663828b, eac7f094, 1873072a, e5e9032d, e1869107, a94ece8f, aa40e67c, aef3fb58, b0790994, b093b4b1, b33047b0, b3e4cb62, b5464409, b59de024, bced4fe7, bf75bd99, bfe7443f, c146e24d, ce5facd3, d582cbc4, d6134922, d66595f1, d8769094, dfc7938d, e2440c2b, 14f08c77, 10a651a8, 2052c101, 260c3f80, 29a28a96, 396e0724, 3cb7aaff, 4b2a15a7, 4e707c75, 5687cbf9, 5b7d0a31, 5ed94ddd, 60ab4ec6, 6909a4f5, 6e728f1c, 786f10b0, aa8f6e25, ac8b4d93, b4821b06, b5725fc4, ba82c4a9, 199aee46, bbc10ec7, 18b9aa97, 09ea14c5, e22f5fb3, 10f34863, 137f2629, 24d115bf, 34ca7601, 5111f317, 60d52f64, 7e2befc9, 85c0808f, 93343cd5, ade7b1c3, c24725fb, cf2d0b85, d8763f40, f50761ce, fd70d28f, 0c1aaf7c, dcef8d5b, ce743c98, dd0a7e48, e2c3fe7c, e621ebec, 0ccdf69d, 6d1a0a56, 8ab17899, af216fda, 265bbad3, 2af99454, 35b1e783, 45e883dc, 908c42e0, a26e68cc, c60ae8a3, 056cd648, 08adc1f1, 0a75e638, 0e22e70c, 0f0cc072, d06f2c03, cc335faa, c7518401, e1444fcf, e2c57ca5, e6777561, ed9b5cb5, f71cbc02, 01c5f16b, 0f6987a3, 102bd77d, 121407c8, 173f2cb3, 2bee29e3, 2e4a4c76, 2f8d073f, 34850db5, 35aebb81, 3857774d, 385e725e, 91990003, b773728c, c81b8c28, 4deec189 | Run miscellaneous shell triage commands (175x) | Bash / PowerShell | Environment, process, filesystem, diagnostics | Right when command domain was shell-native; not a Roslyn target unless it duplicated semantic C# discovery. |

## 2a. Roslyn MCP issues encountered

JSON-aware scan found 1,294 actual mcp__roslyn__* calls. The raw extractor marked 286 issue-shaped results; the table below collapses recurring/actionable failure modes and groups expected invalid-input safe refusals so the report remains triageable.

| Tool | Sessions | Inputs | Symptom | Impact | Workaround | Repro confidence |
|---|---|---|---|---|---|---|
| mcp__roslyn__format_range_apply | 34ca7601, 7e2befc9 | previewToken after matching format_range_preview | PreToolUse apply hook could not read transcript even with previewToken. Quote 34ca7601: "Cannot verify preview was shown without access to transcript." | Blocked formatting apply retries; cumulative several minutes and forced preview/apply churn. | Retry preview, fall back to manual Edit, or avoid apply tools. | intermittent |
| mcp__roslyn__extract_interface_apply / create_file_apply / fix_all_apply / move_type_to_file_apply | 34ca7601, 1d93dd85, d8763f40, aef3fb58 | previewToken from matching preview tools | Same apply-gate failure across multiple apply tools. Quote aef3fb58: "PreToolUse:mcp__roslyn__move_type_to_file_apply hook error". | Apply path looked unsafe even after preview; agents used manual edits or abandoned covered refactors. | Manual Edit/Write or rerun preview/apply after hook context changed. | deterministic across apply gate shape |
| mcp__roslyn__find_references | ac8b4d93, 91990003 | workspaceId + metadataName + limit | Rejected metadataName-only calls. Quote ac8b4d93/91990003: "Invalid argument: Provide either filePath with line and column, or symbolHandle." | Forced Grep fallback when agents had FQN but no cursor; slowed DI/type usage audits. | Use filePath+line+column, symbolHandle, or find_references_bulk where available. | intermittent, same input shape |
| mcp__roslyn__find_references / project_graph / rename_preview / diagnostics tools | 5111f317, 57a0d696, bbc10ec7, 28e53529, c7518401, eac7f094 | previous workspaceId after reload/close/host transition | Workspace IDs went stale. Quote 5111f317: "Workspace ... not found or has been closed. Active workspace IDs are listed by workspace_list." | Broke semantic navigation and forced workspace_list/workspace_load recovery; cumulative 10+ minutes across sessions. | Call workspace_list/workspace_load, refresh workspaceId, then retry. | recurring |
| mcp__roslyn__get_coupling_metrics | ac8b4d93, bfe7443f | workspaceId | Tool was referenced but unavailable. Quote: "No such tool available: mcp__roslyn__get_coupling_metrics". | Coupling audit fell back to complexity/cohesion/manual review. | Use get_cohesion_metrics/get_complexity_metrics/manual inspection. | intermittent/catalog drift |
| mcp__roslyn__get_prompt_text | 34ca7601, d8763f40, dd0a7e48, c7518401, 28e53529 | promptName=discover_capabilities/explain_error/review_file with incomplete parametersJson | Required parameter shape was hard to infer. Quotes include "workspaceId ... required but missing" and "taskCategory ... required but missing". | Prompt discovery attempts failed fast and agents returned to manual docs/tool exploration. | Manually infer prompt parameters or use direct tools. | recurring |
| mcp__roslyn__change_signature_preview | 34ca7601, d8763f40, dd0a7e48 | op=reorder or metadataName-based signature change | Unsupported common signature refactor. Quote: "Parameter reordering is not supported - stage a remove + add pair". | Agents used manual edits for callsite-sensitive signature changes. | Manual Edit plus build/compile_check; sometimes preview_multi_file_edit. | recurring |
| mcp__roslyn__server_info | b33047b0 | {} | Generic failure only. Quote: "An error occurred invoking 'server_info'." | Blocked liveness/surface confirmation in that session; no actionable detail in transcript. | Retry or use shell/server restart context. | one-shot |
| mcp__roslyn__validate_recent_git_changes | d8763f40, 28e53529 | workspaceId + summary/runTests flags | Generic failure only. Quote: "An error occurred invoking 'validate_recent_git_changes'." | Validation bundle could not replace shell build/test in those sessions. | Bash dotnet build/test/verify scripts. | intermittent |
| mcp__roslyn__test_run | 3f08f256, 6c362c08 | workspaceId, sometimes filter | One generic invocation error and one stale workspace NotFound. Quotes: "An error occurred invoking 'test_run'" and "Workspace ... not found or has been closed". | Agents fell back to dotnet test; lost targeted test loop. | Reload workspace or run dotnet test directly. | intermittent |
| mcp__roslyn__many preview/read tools | multiple, especially 34ca7601, 7e2befc9, 1d93dd85, 28e53529 | synthetic invalid handles, nonexistent types, bad ranges, symbols still referenced | Expected safe refusals surfaced as structured InvalidArgument/InvalidOperation rows, e.g. "symbolHandle is not valid base64" or "All selected statements must be in the same block scope." | Mostly audit/test probe noise, not product failures; included here so issue counts are honest. | Correct input, use symbol_info/document_symbols first, or manual redesign. | one-shot per invalid input |

## 2b. Missing tool gaps

| Task | Sessions | Why Roslyn-shaped | Proposed tool shape | Closest existing tool |
|---|---|---|---|---|
| Change a type namespace inside the same project recurring | ac8b4d93, dd0a7e48, backlog evidence read in 35b1e783 | Namespace-cycle fixes are semantic: update declaration, file path, usings, and consumers while staying in the same project. | change_type_namespace_preview(workspaceId, typeHandle/metadataName, targetNamespace, newFilePath?) -> previewToken, file diffs, consumer using changes; apply counterpart. | move_type_to_file keeps namespace; move_type_to_project changes project; move_file does not rewrite namespace. |
| Reference lookup from metadataName/FQN without cursor | ac8b4d93, 91990003 | Agents often get FQNs from DI, diagnostics, outlines, or reports before they have a source cursor. | find_references(workspaceId, metadataName, limit, summary) should resolve type/member FQNs or return candidates. | find_references_bulk reportedly supports metadataName; find_references rejected it. |
| Parameter reorder or replace many parameters with one record recurring | 34ca7601, d8763f40, dd0a7e48 | Signature migrations are semantic and callsite-sensitive. | change_signature_preview(op=reorder/replace_with_parameter_object, target, mapping) -> owner diff + callsite diffs. | change_signature_preview supports add/remove/rename only. |
| Fast semantic edit followed by compile/test verification recurring | many mixed/refactoring sessions; examples 385e725e, d06f2c03, b5725fc4, d66595f1 | Most real changes were pinpoint text edits where full preview/apply felt too heavy, but semantic verification still mattered. | semantic_edit_with_verify(workspaceId, edits[], compile=true, tests=related) -> applied files, diagnostics, related test result, rollback token. | apply_text_edit and compile_check exist separately; no one-call safe edit loop. |
| Restore-aware workspace reload after csproj/package edits | c7518401, IT-Chat-Bot audit evidence in backlog text | MSBuild assets and MetadataReferences are Roslyn workspace inputs, so reload without restore can lie after package graph edits. | workspace_reload(workspaceId, autoRestore=true) -> restoreRequired/restored, asset drift diagnostics, refreshed references. | workspace_load has autoRestore in current surface; historical reload required manual dotnet restore. |
| Find near-duplicate method bodies across solution | self-retro evidence in Roslyn-backed-MCP sessions, e.g. bfe7443f | Duplicate helper detection should be AST/symbol aware, not text-only. | find_duplicated_methods(workspaceId, minLines, similarityThreshold) -> duplicate groups with method handles and diffs. | find_duplicate_helpers is narrower; grep/manual Read was used for internal helper duplicates. |
| Batch scaffold tests with seeded fixtures recurring | 5111f317, b5725fc4, dd0a7e48, c7518401 | Test creation is symbol-shaped but often needs several related test files/fixtures. | scaffold_test_batch_preview(workspaceId, targets[], fixtureHints, framework) -> generated files and arrange/act/assert skeletons. | scaffold_first_test_file_preview/test_discover help single-file discovery but not batch fixture scaffolding. |

## 3. Recurring friction patterns

1. **Apply gate blocks valid preview-token flows.** Sessions 34ca7601, 7e2befc9, 1d93dd85, d8763f40, and aef3fb58 hit PreToolUse:mcp__roslyn__*_apply failures even after previews. Quote 34ca7601: "Cannot verify preview was shown without access to transcript." This recurs on every apply tool protected by transcript scanning; fix it with first-class previewToken validation instead of transcript grep.

2. **Reference APIs are inconsistent around metadata names and stale handles.** Sessions ac8b4d93 and 91990003 show find_references rejecting metadataName; d8763f40, dd0a7e48, and 28e53529 show stale/invalid symbol handles returning NotFound. The recurring cause is that agents often carry FQNs or handles across reloads; fix by accepting metadataName directly and by returning clearer stale-handle recovery instructions.

3. **Workspace lifecycle recovery is too manual.** Sessions 5111f317, 57a0d696, bbc10ec7, 28e53529, c7518401, and eac7f094 all hit "Workspace ... not found or has been closed" on follow-up tools. The pattern recurs when work crosses reloads, workspace_close, or host restarts; fix with automatic workspaceId refresh hints, stable handles, or a recoverable workspace alias.

4. **Catalog/schema drift appears as missing tools or generic invocation failures.** Sessions ac8b4d93 and bfe7443f reported get_coupling_metrics as unavailable; 2bee29e3 and cf2d0b85 saw other named tools missing; b33047b0 and 3f08f256 saw generic invocation failures. The fix is a clearer catalog/surface contract plus structured errors that distinguish removed, experimental, deferred, and disconnected tools.

5. **Mixed self-edit sessions still default to shell/Grep for inner loops.** The window had 139 sessions with build/test shell commands and 119 with Grep/Glob, while 69 made actual Roslyn calls. This is partly correct for CI/git, but code sessions repeatedly used shell build/test and text search where compile_check, test_related_files, and find_references were the intended fast loop. The fix is docs plus a low-friction edit-with-verify tool so agents do not have to choose between speed and semantic safety.

6. **Covered refactors still fail at common composite shapes.** change_signature_preview could not reorder parameters; namespace moves inside one project had no tool; preview/apply shapes did not always expose enough callsite/context to trust broad changes. This recurs in refactoring-heavy and mixed sessions because real changes often cross declarations, usings, callsites, and tests. Add composite preview tools for namespace moves, signature reorder/parameter-object conversion, and callsite diff summaries.

7. **Reload/restore/compile trust is fragile after project graph changes.** Historical IT-Chat-Bot and Roslyn-backed sessions in the window described stale assembly-reference diagnostics and false confidence after reloads; current transcripts also show repeated fallback to dotnet restore/dotnet build. This recurs whenever csproj/package edits happen; fix with restoreRequired/autoRestore signals on reload and fail-loud zero-project compile responses.

## 4. Suggested findings (up to 7)

### 1. preview-token-apply-gate

- **priority hint:** high - repeated across at least 5 sessions and multiple apply tools.
- **title:** Replace transcript-grep apply gating with preview tokens
- **summary:** Apply tools were blocked even after preview. The shared quote was "Cannot verify preview was shown without access to transcript" (34ca7601), with related failures on format, extract-interface, create-file, fix-all, and move-type apply flows.
- **proposed action:** behavior change: require and validate previewToken on apply tools; make hook/token validation deterministic.
- **evidence:** 2a#mcp__roslyn__format_range_apply, 2a#mcp__roslyn__extract_interface_apply, 3#apply-gate-blocks-valid-preview-token-flows; sessions 34ca7601, 7e2befc9, 1d93dd85, d8763f40, aef3fb58.

### 2. find-references-metadata-name

- **priority hint:** high - recurring and directly causes Grep fallback.
- **title:** Accept metadataName in find_references
- **summary:** find_references rejected metadataName-only calls with "Provide either filePath with line and column, or symbolHandle" in ac8b4d93 and 91990003. The caller already had a fully-qualified symbol name, so the fallback was textual search or a cursor hunt.
- **proposed action:** behavior change: resolve metadataName in find_references or remove the parameter from schema/docs if unsupported.
- **evidence:** 2a#mcp__roslyn__find_references, 2b#Reference lookup from metadataName/FQN without cursor, 3#reference-apis-are-inconsistent; sessions ac8b4d93, 91990003.

### 3. workspace-id-recovery

- **priority hint:** medium - broad recurrence, but often recoverable manually.
- **title:** Make stale workspace recovery first-class
- **summary:** Multiple tools returned "Workspace ... not found or has been closed" across 5111f317, 57a0d696, bbc10ec7, 28e53529, c7518401, and eac7f094. The workaround was always workspace_list/workspace_load plus retry.
- **proposed action:** behavior change and error-message fix: include active workspace aliases, exact reload command, and optional auto-reload by loaded path.
- **evidence:** 2a#mcp__roslyn__find_references, 3#workspace-lifecycle-recovery-is-too-manual; sessions 5111f317, 57a0d696, bbc10ec7, 28e53529, c7518401, eac7f094.

### 4. catalog-schema-drift-errors

- **priority hint:** medium - repeated missing-tool and generic-error symptoms across audit sessions.
- **title:** Clarify unavailable tool and invocation failure states
- **summary:** get_coupling_metrics returned "No such tool available" in ac8b4d93 and bfe7443f, while server_info, test_run, and validate_recent_git_changes sometimes returned only "An error occurred invoking ...". These messages do not tell the maintainer whether the tool is removed, experimental, disconnected, or crashed.
- **proposed action:** error-message fix and docs: structured unavailable-tool reason plus catalog version/surface tier in error payloads.
- **evidence:** 2a#mcp__roslyn__get_coupling_metrics, 2a#mcp__roslyn__server_info, 3#catalog-schema-drift; sessions ac8b4d93, bfe7443f, b33047b0, d8763f40, 28e53529, 3f08f256.

### 5. change-type-namespace-preview

- **priority hint:** medium - strong semantic gap, but direct evidence is concentrated in a few sessions.
- **title:** Add same-project namespace move preview/apply
- **summary:** Namespace-cycle fixes needed declaration, file path, using, and consumer updates, but no single tool fit. Existing moves either keep namespace or change project, so agents used Write/Edit/rm/Grep chains.
- **proposed action:** new tool: change_type_namespace_preview/apply with consumer using updates and optional file relocation.
- **evidence:** 2b#Change a type namespace inside the same project, 3#covered-refactors-still-fail-at-common-composite-shapes; sessions ac8b4d93, dd0a7e48, 35b1e783.

### 6. semantic-edit-with-verify

- **priority hint:** medium - high volume of manual edits and shell verification, but broad tool design work.
- **title:** Provide a low-friction semantic edit with verify loop
- **summary:** The window had 73 sessions editing C# or project files and 139 sessions running build/test/verify shell commands. Agents often chose fast Edit/Write for pinpoint changes, then paid shell validation cost; a one-call edit/compile/related-test wrapper would preserve speed while keeping Roslyn verification in the loop.
- **proposed action:** new tool: semantic_edit_with_verify or extend apply_text_edit with compile/test/rollback options.
- **evidence:** 2#Task inventory, 2b#Fast semantic edit followed by compile/test verification, 3#mixed-self-edit-sessions-default-to-shell-grep; sessions 385e725e, d06f2c03, b5725fc4, d66595f1.

### 7. workspace-reload-auto-restore

- **priority hint:** medium - recurring after package/project edits; may be scale/project specific.
- **title:** Add restore-aware workspace reload signals
- **summary:** Sessions described stale assembly-reference diagnostics after project/package edits and then fell back to dotnet restore plus another reload/build. The current surface already has autoRestore on load; reload needs equivalent behavior or a clear restoreRequired signal.
- **proposed action:** behavior change: workspace_reload(autoRestore=true) and fail-loud compile responses when reload produces zero usable projects or stale references.
- **evidence:** 2b#Restore-aware workspace reload after csproj/package edits, 3#reload-restore-compile-trust-is-fragile; sessions c7518401 plus IT-Chat-Bot audit evidence captured in window transcripts.

## 5. Meta-note

The window is mixed-heavy: 42 refactoring / 21 release-operational / 37 planning-docs / 98 mixed across 198 included sessions. Friction concentrates in reliability and ergonomics: preview/apply gating, workspace lifecycle, catalog/schema drift, and low-friction verification, rather than pure absence of semantic read tools. Repo skew is significant: 94 of 198 included sessions came from roslyn-backed-mcp, and many findings are self-edit or audit-prompt shaped, so future retros should compare a consumer-heavy window before promoting every item. Next time I would default to workspace_load + read-only Roslyn verification earlier in self-edit sessions and reserve shell dotnet build/test for CI parity. The 14-day window was long enough for recurring patterns, but several proposed gaps still lean on 1-2 high-signal sessions; widening to 30 days would improve priority confidence.

MCP availability note for this run: .mcp.json declares a roslyn stdio server via roslynmcp, ai_docs/runtime.md documents Roslyn MCP as the default C# tool surface, and the live current session verified server_info for roslyn-mcp 1.33.0 with zero loaded workspaces.

