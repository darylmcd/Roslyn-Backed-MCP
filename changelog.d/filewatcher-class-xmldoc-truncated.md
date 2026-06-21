---
category: Maintenance
---

- **Maintenance:** Completed the truncated class-level XML-doc clause on `FileWatcherService`. The Attribution-rule `<para>` trailed off at "…server apply paths that want to preserve their attribution mark after the on-disk commit settles." with no main verb; it now reads "…call `MarkStale` once the write lands, overwriting any `external-edit` stamp the watcher may have set." — supplying the missing predicate, parallel to the read-side clause and consistent with the `MarkStale` before/after last-writer-wins semantics documented above. Closes `filewatcher-class-xmldoc-truncated`.
