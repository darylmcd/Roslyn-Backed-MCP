---
category: Fixed
---

- **Fixed:** The experimental `move_type_to_project_preview` and `change_type_namespace_preview` tools refuse a move when the destination already declares the same type identity, including when separate partial types would silently merge (`cross-project-move-receiver-type-collision`).
