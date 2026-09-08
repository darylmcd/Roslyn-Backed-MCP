# copilot-self-edit-mutation-policy-consistency — Make self-edit mutation policy session-aware

**row:** `copilot-self-edit-mutation-policy-consistency` · **pri:** `Low` · **size:** `M`

## Anchors

- `.github/copilot-instructions.md`

## Acceptance

- [ ] Qualify the blanket preview-to-apply rule by session shape.
- [ ] Require preview-to-apply for peer repositories and isolated worktrees, but preview-to-`Edit`/`Write` when the server binary is running from the checkout being edited.
- [ ] Keep one active-instruction inventory that rejects an unconditional mutation rule while allowing examples scoped to a named session shape.

## Evidence

- `.github/copilot-instructions.md:37-46` defines a correct session-aware self-edit policy, but line 50 immediately restates an unconditional preview-to-apply requirement.
