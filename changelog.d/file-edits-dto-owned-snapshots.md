---
category: Fixed
---
- **Fixed:** Store immutable edit snapshots in FileEditsDto during construction and init assignment so caller collection mutation cannot change its value or hash; preserve named construction, deconstruction, JSON shape and ordered structural equality.
