# Plan adversarial review

| Field | Value |
|---|---|
| Plan | 20261007T211247Z_backlog-remediate |
| Cycle | 1 |
| Outcome | failed |
| Counts | 2 block / 8 warn / 20 info |
| Anchor verification | performed |

Reviewed all 29 pending initiatives, all 30 exact stanza artifacts and the complete dependency/conflict graph. Seven split children replace lifecycle/fork parents and resolve those parents' fanout blocks. Shared safety still carries both original blocks. Blocks fell from four to two; none is new. Fork stages depend on the blocked safety initiative. Guard and editorconfig classifier artifacts have double-sided corpora, measured TP/FP/FN and decidable static inputs. No direct classifier route, unknown dependency target, bundle violation or new context/fanout block was found.

## Findings

| Initiative | Severity | Rule | Evidence |
|---|---|---|---|
| preview-store-explicit-internal-state | block | 5b | /s/t/a/t/e/./j/s/o/n/ /r/e/c/o/r/d/s/ /f/a/n/o/u/t/O/v/e/r/s/i/z/e/=/t/r/u/e/,/ /f/a/n/o/u/t/E/s/t/i/m/a/t/e/=/3/8/,/ /p/r/o/d/u/c/t/i/o/n/F/i/l/e/s/T/o/u/c/h/e/d/=/3/8/;/ /h/e/r/o/i/c/-/l/a/s/t/ /d/o/e/s/ /n/o/t/ /w/a/i/v/e/ /t/h/i/s/ /h/a/r/d/ /b/l/o/c/k/./ /S/p/l/i/t/ /i/n/t/o/ /i/n/d/e/p/e/n/d/e/n/t/l/y/ /c/o/r/r/e/c/t/ /d/e/p/e/n/d/e/n/c/y/-/c/h/a/i/n/e/d/ /s/t/a/g/e/s/ /a/n/d/ /c/o/l/d/-/r/e/v/i/e/w/ /e/v/e/r/y/ /c/h/i/l/d/ /b/e/f/o/r/e/ /e/x/e/c/u/t/i/o/n/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| preview-store-explicit-internal-state | block | 5 | /e/s/t/i/m/a/t/e/d/C/o/n/t/e/x/t/T/o/k/e/n/s/=/1/1/0/0/0/0/ /e/x/c/e/e/d/s/ /t/h/e/ /a/u/t/h/o/r/i/t/a/t/i/v/e/ /r/u/l/e/5/-/m/a/x/-/c/o/n/t/e/x/t/-/t/o/k/e/n/s/ /m/a/r/k/e/r/ /8/0/0/0/0/ /i/n/ /b/a/c/k/l/o/g/-/r/e/m/e/d/i/a/t/e/-/r/u/l/e/s/./m/d/:/1/1/6/./ /T/h/e/ /s/t/a/n/z/a/ /i/t/s/e/l/f/ /r/e/q/u/i/r/e/s/ /s/p/l/i/t/ /b/e/f/o/r/e/ /e/x/e/c/u/t/i/o/n/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| navigation-and-locator-argument-refusals-public-message | info | 3 | /S/y/m/b/o/l/R/e/s/o/l/v/e/r/./c/s/:/2/9/1/ /u/n/c/h/e/c/k/e/d/ /a/d/d/i/t/i/o/n/ /r/e/a/c/h/e/s/ /F/i/n/d/T/o/k/e/n/(/:/2/9/6/)/;/ /t/h/e/ /h/e/l/p/e/r/ /a/n/d/ /a/l/l/ /r/a/w/-/p/o/s/i/t/i/o/n/ /c/o/n/s/u/m/e/r/s/ /f/o/r/m/ /o/n/e/ /c/o/m/p/l/e/t/e/ /b/o/u/n/d/s///p/u/b/l/i/c/-/c/o/r/r/e/c/t/i/o/n/ /c/h/a/n/g/e/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| scaffold-batch-preview-apply-route | info | 3 | /B/a/t/c/h/T/e/s/t/S/c/a/f/f/o/l/d/e/r/./c/s/:/2/3/6/ /s/t/o/r/e/s/ /u/n/t/a/g/g/e/d/ /s/o/l/u/t/i/o/n/ /p/r/e/v/i/e/w/s/;/ /O/r/c/h/e/s/t/r/a/t/i/o/n/T/o/o/l/s/./c/s/:/1/2/4/-/1/2/8/ /p/e/e/k/s/ /o/n/l/y/ /c/o/m/p/o/s/i/t/e/ /s/t/o/r/a/g/e/;/ /S/e/r/v/e/r/S/u/r/f/a/c/e/C/a/t/a/l/o/g/./c/s/:/4/3/-/8/5/ /o/m/i/t/s/ /b/a/t/c/h/ /p/a/i/r/i/n/g/./ /P/r/o/d/u/c/e/r/,/ /c/o/r/r/e/c/t/i/o/n/ /a/n/d/ /c/a/t/a/l/o/g/ /c/o/m/p/a/n/i/o/n/s/ /s/h/a/r/e/ /t/h/e/ /o/b/s/e/r/v/e/d/ /r/o/u/t/e/ /d/e/f/e/c/t/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| restore-callers-missing-packages-path | info | 3 | /R/e/s/t/o/r/e/S/t/a/l/e/n/e/s/s/D/e/t/e/c/t/o/r/./c/s/:/4/0/5/-/4/0/6/ /u/s/e/s/ /T/r/y/G/e/t/P/r/o/p/e/r/t/y/ /w/i/t/h/o/u/t/ /r/o/o/t///p/r/o/j/e/c/t/ /o/b/j/e/c/t/ /g/u/a/r/d/s/;/ /W/o/r/k/s/p/a/c/e/T/o/o/l/s/./c/s/:/7/0/7/ /c/o/n/f/l/a/t/e/s/ /p/a/c/k/a/g/e/-/r/o/o/t/ /i/d/e/n/t/i/t/y/./ /E/x/i/s/t/i/n/g/ /f/o/r/k/ /c/o/p/y/i/n/g/ /o/m/i/t/s/ /a/s/s/e/t/s/,/ /f/o/r/c/i/n/g/ /s/o/u/r/c/e/-/c/a/p/t/u/r/e/d/ /s/h/a/r/e/d/ /r/e/s/t/o/r/e/ /p/l/a/n/n/i/n/g/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| preview-store-explicit-internal-state | info | 3 | /P/r/e/v/i/e/w/S/t/o/r/e/./c/s/:/6/8/-/8/8/ /d/e/f/a/u/l/t/s/ /c/o/m/p/l/e/t/e/n/e/s/s///p/r/o/v/e/n/a/n/c/e/;/ /B/a/t/c/h/T/e/s/t/S/c/a/f/f/o/l/d/e/r/./c/s/:/2/3/6/ /d/i/s/c/a/r/d/s/ /c/o/m/p/u/t/e/d/ /c/h/a/n/g/e/s/;/ /D/i/f/f/G/e/n/e/r/a/t/o/r/./c/s/:/8/3/-/1/0/0/ /p/r/o/d/u/c/e/s/ /p/e/r/-/f/i/l/e/ /t/r/u/n/c/a/t/i/o/n/./ /T/o/o/l/D/i/s/p/a/t/c/h/T/e/s/t/s/./c/s/:/2/3/6/-/2/6/7/ /a/n/d/ /2/7/8/-/3/1/6/ /v/e/r/i/f/y/ /e/v/e/r/y/ /c/o/n/c/r/e/t/e/ /P/r/e/v/i/e/w/K/i/n/d/ /r/o/u/t/e/ /a/n/d/ /i/t/s/ /c/o/n/t/e/n/t/ /p/i/n/./ /P/r/o/d/u/c/e/r///c/o/n/t/r/a/c/t/s///p/e/r/s/i/s/t/e/n/c/e///g/u/a/r/d/s///m/a/p/s/ /f/o/r/m/ /t/h/e/ /t/r/a/c/e/d/ /s/a/f/e/t/y/ /m/e/c/h/a/n/i/s/m/,/ /s/t/i/l/l/ /b/l/o/c/k/e/d/ /p/e/n/d/i/n/g/ /s/p/l/i/t/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| preview-token-lifecycle-evidence | info | 3 | /S/i/x/ /p/r/o/d/u/c/t/i/o/n/ /f/i/l/e/s/ /f/o/r/m/ /t/h/e/ /t/r/a/c/e/d/ /r/e/m/o/v/a/l/-/e/v/i/d/e/n/c/e///s/o/u/r/c/e/-/c/o/n/t/r/a/c/t/ /u/n/i/t/:/ /B/o/u/n/d/e/d/S/t/o/r/e/./c/s/:/3/5/-/5/1/ /r/e/m/o/v/e/s/ /p/a/y/l/o/a/d/ /w/i/t/h/o/u/t/ /c/a/u/s/e/,/ /P/r/e/v/i/e/w/S/t/o/r/e/./c/s/:/2/7/9/-/2/8/4/ /i/n/v/a/l/i/d/a/t/e/s/ /o/n/ /r/e/l/o/a/d/,/ /a/n/d/ /t/h/r/e/e/ /s/t/o/r/e/ /i/n/t/e/r/f/a/c/e/s/ /r/e/q/u/i/r/e/ /e/x/p/l/i/c/i/t/ /c/o/m/p/l/e/t/i/o/n///q/u/e/r/y/ /o/p/e/r/a/t/i/o/n/s/./ /S/c/o/p/e/ /i/n/c/l/u/d/e/s/ /c/o/n/c/r/e/t/e/ /f/a/k/e/ /e/d/i/t/s/ /a/n/d/ /A/D/R/ /m/i/g/r/a/t/i/o/n/;/ /n/i/n/e/ /t/e/s/t/ /f/i/l/e/s/ /e/x/e/r/c/i/s/e/ /o/n/e/ /l/i/f/e/c/y/c/l/e/ /m/e/c/h/a/n/i/s/m/./ |
| preview-token-reason-projection | info | 3 | /E/i/g/h/t/ /p/r/o/d/u/c/t/i/o/n/ /f/i/l/e/s/ /f/o/r/m/ /o/n/e/ /r/e/a/s/o/n/ /p/r/o/p/a/g/a/t/i/o/n/ /c/h/a/n/g/e/:/ /T/o/o/l/D/i/s/p/a/t/c/h/./c/s/:/3/9/8/ /c/o/n/s/t/r/u/c/t/s/ /a/ /c/a/u/s/e/-/a/g/n/o/s/t/i/c/ /e/x/c/e/p/t/i/o/n/;/ /T/o/o/l/E/r/r/o/r/H/a/n/d/l/e/r/./c/s/:/1/4/6/-/1/5/1/ /m/a/p/s/ /e/v/e/r/y/ /m/i/s/s/ /t/o/ /r/e/l/o/a/d/./ /R/e/q/u/i/r/e/d/ /e/x/c/e/p/t/i/o/n///d/e/l/e/g/a/t/e/ /c/h/a/n/g/e/s/ /i/n/c/l/u/d/e/ /t/h/e/ /l/i/s/t/e/d/ /p/r/o/d/u/c/t/i/o/n/ /c/o/n/s/t/r/u/c/t/o/r/ /a/n/d/ /g/e/n/e/r/i/c/-/a/p/p/l/y/ /c/a/l/l/e/r/s/./ |
| preview-token-lifecycle-evidence | warn | C2-wave-conflict | /C/o/n/s/e/c/u/t/i/v/e/ /o/r/d/e/r/ /7/./0/2/ /a/n/d/ /8/./0/1/ /o/v/e/r/l/a/p/ /o/n/ /1/ /f/i/l/e/(/s/)/:/ /d/o/c/s///d/e/c/i/s/i/o/n/s///R/E/A/D/M/E/./m/d/./ /U/s/e/ /d/i/s/t/i/n/c/t/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /l/a/t/e/r/ /s/c/o/p/e/s/ /a/f/t/e/r/ /e/a/r/l/i/e/r/ /l/a/n/d/i/n/g/./ |
| preview-token-reason-projection | warn | C2-wave-conflict | /C/o/n/s/e/c/u/t/i/v/e/ /o/r/d/e/r/ /8/./0/1/ /a/n/d/ /8/./0/2/ /o/v/e/r/l/a/p/ /o/n/ /2/ /f/i/l/e/(/s/)/:/ /d/o/c/s///d/e/c/i/s/i/o/n/s///0/0/2/4/-/p/r/e/v/i/e/w/-/t/o/k/e/n/-/t/e/r/m/i/n/a/l/-/l/i/f/e/c/y/c/l/e/./m/d/,/ /t/e/s/t/s///R/o/s/l/y/n/M/c/p/./T/e/s/t/s///T/o/o/l/D/i/s/p/a/t/c/h/T/e/s/t/s/./c/s/./ /U/s/e/ /d/i/s/t/i/n/c/t/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /l/a/t/e/r/ /s/c/o/p/e/s/ /a/f/t/e/r/ /e/a/r/l/i/e/r/ /l/a/n/d/i/n/g/./ |
| preview-token-solution-confirmation | warn | C2-wave-conflict | /C/o/n/s/e/c/u/t/i/v/e/ /o/r/d/e/r/ /8/./0/2/ /a/n/d/ /8/./0/3/ /o/v/e/r/l/a/p/ /o/n/ /1/ /f/i/l/e/(/s/)/:/ /t/e/s/t/s///R/o/s/l/y/n/M/c/p/./T/e/s/t/s///P/r/e/v/i/e/w/T/o/k/e/n/C/o/n/s/u/m/e/d/R/e/a/s/o/n/T/e/s/t/s/./c/s/./ /U/s/e/ /d/i/s/t/i/n/c/t/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /l/a/t/e/r/ /s/c/o/p/e/s/ /a/f/t/e/r/ /e/a/r/l/i/e/r/ /l/a/n/d/i/n/g/./ |
| preview-token-composite-confirmation | warn | C2-wave-conflict | /C/o/n/s/e/c/u/t/i/v/e/ /o/r/d/e/r/ /8/./0/3/ /a/n/d/ /8/./0/4/ /o/v/e/r/l/a/p/ /o/n/ /1/ /f/i/l/e/(/s/)/:/ /t/e/s/t/s///R/o/s/l/y/n/M/c/p/./T/e/s/t/s///P/r/e/v/i/e/w/T/o/k/e/n/C/o/n/s/u/m/e/d/R/e/a/s/o/n/T/e/s/t/s/./c/s/./ /U/s/e/ /d/i/s/t/i/n/c/t/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /l/a/t/e/r/ /s/c/o/p/e/s/ /a/f/t/e/r/ /e/a/r/l/i/e/r/ /l/a/n/d/i/n/g/./ |
| preview-token-project-confirmation | warn | C2-wave-conflict | /C/o/n/s/e/c/u/t/i/v/e/ /o/r/d/e/r/ /8/./0/4/ /a/n/d/ /8/./0/5/ /o/v/e/r/l/a/p/ /o/n/ /1/ /f/i/l/e/(/s/)/:/ /t/e/s/t/s///R/o/s/l/y/n/M/c/p/./T/e/s/t/s///P/r/e/v/i/e/w/T/o/k/e/n/C/o/n/s/u/m/e/d/R/e/a/s/o/n/T/e/s/t/s/./c/s/./ /U/s/e/ /d/i/s/t/i/n/c/t/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /l/a/t/e/r/ /s/c/o/p/e/s/ /a/f/t/e/r/ /e/a/r/l/i/e/r/ /l/a/n/d/i/n/g/./ |
| preview-diff-whitespace-omission | warn | C2-wave-conflict | /C/o/n/s/e/c/u/t/i/v/e/ /o/r/d/e/r/ /2/5/ /a/n/d/ /2/6/ /o/v/e/r/l/a/p/ /o/n/ /6/ /f/i/l/e/(/s/)/:/ /s/r/c///R/o/s/l/y/n/M/c/p/./R/o/s/l/y/n///H/e/l/p/e/r/s///D/i/f/f/G/e/n/e/r/a/t/o/r/./c/s/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./R/o/s/l/y/n///H/e/l/p/e/r/s///S/o/l/u/t/i/o/n/D/i/f/f/H/e/l/p/e/r/./c/s/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./R/o/s/l/y/n///S/e/r/v/i/c/e/s///E/d/i/t/S/e/r/v/i/c/e/./c/s/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./R/o/s/l/y/n///S/e/r/v/i/c/e/s///R/e/f/a/c/t/o/r/i/n/g/S/e/r/v/i/c/e/./c/s/,/ /t/e/s/t/s///R/o/s/l/y/n/M/c/p/./T/e/s/t/s///D/i/f/f/G/e/n/e/r/a/t/o/r/T/e/s/t/s/./c/s/,/ /t/e/s/t/s///R/o/s/l/y/n/M/c/p/./T/e/s/t/s///S/o/l/u/t/i/o/n/D/i/f/f/H/e/l/p/e/r/T/e/s/t/s/./c/s/./ /U/s/e/ /d/i/s/t/i/n/c/t/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /l/a/t/e/r/ /s/c/o/p/e/s/ /a/f/t/e/r/ /e/a/r/l/i/e/r/ /l/a/n/d/i/n/g/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| scaffold-batch-preview-apply-route | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /4/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| restore-callers-missing-packages-path | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /4/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| change-signature-class-struct-primary-ctor-add-remove | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /2/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| workspace-close-global-build-server-shutdown | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /7/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| change-signature-service-refusals-public-message | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /2/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| cross-project-public-refusals-echo-input | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /4/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| preview-diff-whitespace-omission | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /2/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ /S/t/i/l/l/ /p/r/e/s/e/n/t/ /a/f/t/e/r/ /c/y/c/l/e/ /0/ /r/e/m/e/d/i/a/t/i/o/n/./ |
| preview-token-lifecycle-evidence | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /7/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| preview-token-reason-projection | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /9/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| preview-token-solution-confirmation | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /6/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| preview-token-composite-confirmation | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /5/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| preview-token-project-confirmation | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /4/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| fork-composite-nonconsuming-snapshot | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /2/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| fork-preview-project-composite-replay | info | C2 | /C/o/n/f/l/i/c/t/ /d/e/g/r/e/e/ /7/ /w/i/t/h/o/u/t/ /h/e/r/o/i/c/-/l/a/s/t/;/ /u/s/e/ /c/o/m/p/u/t/e/d/ /g/e/n/e/r/a/t/i/o/n/s/ /a/n/d/ /r/e/d/e/r/i/v/e/ /a/f/t/e/r/ /o/v/e/r/l/a/p/p/i/n/g/ /p/r/e/d/e/c/e/s/s/o/r/s/ /l/a/n/d/./ |
| fork-composite-nonconsuming-snapshot | warn | hotspot | /C/o/n/s/e/c/u/t/i/v/e/ /i/n/i/t/i/a/t/i/v/e/s/ /6/ /(/s/c/a/f/f/o/l/d/-/b/a/t/c/h/-/p/r/e/v/i/e/w/-/a/p/p/l/y/-/r/o/u/t/e/)/ /a/n/d/ /7/./0/1/ /b/o/t/h/ /t/o/u/c/h/ /a/d/d/e/n/d/a/ /h/o/t/s/p/o/t/s/:/ /R/E/A/D/M/E/./m/d/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./H/o/s/t/./S/t/d/i/o///R/E/A/D/M/E/./m/d/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./H/o/s/t/./S/t/d/i/o///C/a/t/a/l/o/g///S/e/r/v/e/r/S/u/r/f/a/c/e/C/a/t/a/l/o/g/./O/r/c/h/e/s/t/r/a/t/i/o/n/./c/s/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./H/o/s/t/./S/t/d/i/o///C/a/t/a/l/o/g///S/e/r/v/e/r/S/u/r/f/a/c/e/C/a/t/a/l/o/g/./c/s/;/ /s/r/c///R/o/s/l/y/n/M/c/p/./R/o/s/l/y/n///S/e/r/v/i/c/e/C/o/l/l/e/c/t/i/o/n/E/x/t/e/n/s/i/o/n/s/./c/s/./ /R/e/s/p/e/c/t/ /o/n/e/ /h/o/t/s/p/o/t/-/t/o/u/c/h/i/n/g/ /i/n/i/t/i/a/t/i/v/e/ /p/e/r/ /w/a/v/e/ /e/v/e/n/ /w/h/e/r/e/ /e/x/a/c/t/ /f/i/l/e/s/ /d/i/f/f/e/r/./ |
| fork-preview-project-composite-replay | warn | hotspot | /C/o/n/s/e/c/u/t/i/v/e/ /i/n/i/t/i/a/t/i/v/e/s/ /7/./0/1/ /(/f/o/r/k/-/c/o/m/p/o/s/i/t/e/-/n/o/n/c/o/n/s/u/m/i/n/g/-/s/n/a/p/s/h/o/t/)/ /a/n/d/ /7/./0/2/ /b/o/t/h/ /t/o/u/c/h/ /a/d/d/e/n/d/a/ /h/o/t/s/p/o/t/s/:/ /s/r/c///R/o/s/l/y/n/M/c/p/./R/o/s/l/y/n///S/e/r/v/i/c/e/C/o/l/l/e/c/t/i/o/n/E/x/t/e/n/s/i/o/n/s/./c/s/;/ /R/E/A/D/M/E/./m/d/,/ /s/r/c///R/o/s/l/y/n/M/c/p/./H/o/s/t/./S/t/d/i/o///R/E/A/D/M/E/./m/d/./ /R/e/s/p/e/c/t/ /o/n/e/ /h/o/t/s/p/o/t/-/t/o/u/c/h/i/n/g/ /i/n/i/t/i/a/t/i/v/e/ /p/e/r/ /w/a/v/e/ /e/v/e/n/ /w/h/e/r/e/ /e/x/a/c/t/ /f/i/l/e/s/ /d/i/f/f/e/r/./ |

## Conflict graph

Stored agreement: true. Rebuilt Scope production/test/doc union, expanding stanza path groups against exact arrays, normalizing paths and excluding shared backlog/changelog fragments. Consecutive sorted positions determine fractional-order adjacency.

```json
{
  "edges": [
    {
      "a": 2,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CodeActionService.cs"
      ]
    },
    {
      "a": 3,
      "b": 11,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs"
      ]
    },
    {
      "a": 5,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs"
      ]
    },
    {
      "a": 6,
      "b": 7.02,
      "sharedFiles": [
        "README.md",
        "src/RoslynMcp.Host.Stdio/README.md"
      ]
    },
    {
      "a": 6,
      "b": 8.01,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 6,
      "b": 8.02,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/OrchestrationTools.cs",
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 6,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs",
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs",
        "tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 7.01,
      "b": 8.04,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 7.01,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 7.02,
      "b": 8.01,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 8.02,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs"
      ]
    },
    {
      "a": 7.02,
      "b": 15,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 7.02,
      "b": 19,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 7.02,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.02,
      "sharedFiles": [
        "docs/decisions/0024-preview-token-terminal-lifecycle.md",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 8.03,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs"
      ]
    },
    {
      "a": 8.01,
      "b": 19,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 8.01,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 8.01,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
        "src/RoslynMcp.Core/Services/ICompositePreviewStore.cs",
        "src/RoslynMcp.Core/Services/IProjectMutationPreviewStore.cs",
        "src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PreviewStore.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs",
        "tests/RoslynMcp.Tests/ApplyWithVerifyCancellationAndScopeTests.cs",
        "tests/RoslynMcp.Tests/BoundedStoreEvictionTests.cs",
        "tests/RoslynMcp.Tests/ExtractionApplyRouteBindingTests.cs",
        "tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingEditingTests.cs",
        "tests/RoslynMcp.Tests/PreviewRouteBindingFileOpsTests.cs",
        "tests/RoslynMcp.Tests/PreviewStoreTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 8.03,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 8.04,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 8.05,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 15,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs"
      ]
    },
    {
      "a": 8.02,
      "b": 19,
      "sharedFiles": [
        "docs/product-contract.md"
      ]
    },
    {
      "a": 8.02,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs",
        "tests/RoslynMcp.Tests/PreviewTokenStaleAcrossAutoReloadTests.cs",
        "tests/RoslynMcp.Tests/ToolDispatchTests.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 8.04,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 8.05,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs"
      ]
    },
    {
      "a": 8.03,
      "b": 26,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs"
      ]
    },
    {
      "a": 8.04,
      "b": 8.05,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/PreviewTokenConsumedReasonTests.cs"
      ]
    },
    {
      "a": 8.04,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs",
        "src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs",
        "src/RoslynMcp.Roslyn/Services/PersistentCompositeStorage.cs",
        "tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs",
        "tests/RoslynMcp.Tests/Services/PersistentCompositeStorageTests.cs"
      ]
    },
    {
      "a": 8.05,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ProjectMutationService.cs",
        "tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs"
      ]
    },
    {
      "a": 12,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/FixAllService.cs"
      ]
    },
    {
      "a": 15,
      "b": 19,
      "sharedFiles": [
        "src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs"
      ]
    },
    {
      "a": 15,
      "b": 25,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs"
      ]
    },
    {
      "a": 16,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs"
      ]
    },
    {
      "a": 17,
      "b": 21,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs",
        "tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs"
      ]
    },
    {
      "a": 17,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs",
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs",
        "tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs"
      ]
    },
    {
      "a": 19,
      "b": 22,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 19,
      "b": 24,
      "sharedFiles": [
        "tests/RoslynMcp.Tests/AnalyzerShadowLoaderLifecycleTests.cs"
      ]
    },
    {
      "a": 19,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md"
      ]
    },
    {
      "a": 21,
      "b": 25,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs",
        "tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs"
      ]
    },
    {
      "a": 22,
      "b": 25,
      "sharedFiles": [
        "docs/decisions/README.md",
        "src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs"
      ]
    },
    {
      "a": 25,
      "b": 26,
      "sharedFiles": [
        "src/RoslynMcp.Roslyn/Helpers/DiffGenerator.cs",
        "src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs",
        "src/RoslynMcp.Roslyn/Services/EditService.cs",
        "src/RoslynMcp.Roslyn/Services/RefactoringService.cs",
        "tests/RoslynMcp.Tests/DiffGeneratorTests.cs",
        "tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs"
      ]
    }
  ],
  "degrees": {
    "2": 1,
    "3": 1,
    "4": 0,
    "5": 1,
    "6": 4,
    "9": 0,
    "10": 0,
    "11": 1,
    "12": 1,
    "13": 0,
    "14": 0,
    "15": 4,
    "16": 1,
    "17": 2,
    "18": 0,
    "19": 7,
    "20": 0,
    "21": 2,
    "22": 4,
    "23": 0,
    "24": 1,
    "25": 18,
    "26": 2,
    "8.01": 7,
    "8.02": 9,
    "8.03": 6,
    "8.04": 5,
    "8.05": 4,
    "7.01": 2,
    "7.02": 7
  },
  "zeroDegreeInitiatives": [
    4,
    9,
    10,
    13,
    14,
    18,
    20,
    23
  ]
}
```

## Hotspots

| Earlier | Later | Earlier hotspots | Later hotspots |
|---|---|---|---|
| 6 | 7.01 | README.md, src/RoslynMcp.Host.Stdio/README.md, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Orchestration.cs, src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs | src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs |
| 7.01 | 7.02 | src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs | README.md, src/RoslynMcp.Host.Stdio/README.md |

## Stale rows

| Check | Result |
|---|---|
| Pending closure rows | All present in current backlog |
| Intermediate split rows | Close none; final stages close original row |
| Terminal | One obsolete initiative skipped for per-initiative checks; retained in graphs |

## Evidence and next step

First-three pending source anchors resolve: SymbolResolver.cs:286-296, WorkspaceManager.cs:1443-1456 and PublicInvalidOperationException.cs:26-28. Current source verifies BoundedStore.cs:26-33/110-119 exceeds its capacity and erases lifecycle causes; PreviewStore.cs:166-176 checks only aggregate sentinel; BatchTestScaffolder.cs:236 drops safety/provenance; ProjectMutationService.cs:646-648 drops safety; PersistentCompositeStorage.cs:154-195 destructively claims payload. Existing lifecycle/safety/fork rows own these defects. Split safety into independently correct dependency stages, then cold-review every child and rewire fork prerequisites through the sanctioned writer. Do not clear fanoutOversize or lower the estimate simply to pass. Admit unrelated eligible work only through canonical gates and graph generations.

Whole-plan canonical assertAcyclicDependsOn passed; no unknown targets. Roslyn server_info verified live; exact worktree solution loaded and symbol_search succeeded. Missing analyzer output leaves buildRequired/restoreRequired readiness flags. No tests/builds or repository/state writes occurred. Windows no-follow CreateFileW directory/file handles, attribute checks, handle-path normalization/containment and bounded 5121-byte reads verified every exact stanza. No scopeInitiativeIds narrowing applied.

