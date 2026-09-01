---
name: Pull Request
about: Submit changes to FeatureDeck
title: ""
labels: ''
assignees: ''
---

## Summary

<!-- What does this PR do? One or two sentences. -->

## Related issue

<!-- If this PR fixes an issue, write: Fixes #123 -->

## Changes

<!-- Bullet list of the concrete changes -->

## How tested

<!-- How did you verify this works? Include Windows build and FeatureDeck version tested on -->

## Checklist

- [ ] Builds with 0 errors: `dotnet build -c Release -p:Platform=x64`
- [ ] No hardcoded UI strings; zh-CN and en-US `Resources.resw` both updated if UI text changed
- [ ] No `bin/`/`obj/` files included in the PR
- [ ] App launch verified (and the scenario changed)
