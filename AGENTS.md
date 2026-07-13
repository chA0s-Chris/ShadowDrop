# Root AGENTS.md

`ShadowDrop` is a secure file-sharing solution.

## Implementation rules

Plans typically have acceptance criteria with check boxes. Check each box when you are finished with the corresponding criterion.

## General Rules for the Code Base

TBD

### Code Style

For the project's code style, refer to `CODESTYLE.md`.

## Production Code Rules

Read ./src/AGENTS.md for details about the production code.

## Testing Rules

Read ./tests/AGENTS.md for details about how to write tests.

## Plan Rules

Read ./ai-plans/AGENTS.md for details on how to write plans.

## Here is Your Space

If you encounter something worth noting while you are working on this code base, write it down here in this section. Once you are finished, I will discuss it with you, and we can decide where to put your notes.

- On Linux kernel 6.19+, the current `mongo:8.3` image exits during startup and cites `SERVER-121912` because the image bakes in `GLIBC_TUNABLES=glibc.pthread.rseq=0`. The Compose smoke target injects `glibc.pthread.rseq=1` through its temporary ignored override, while the operator-facing Compose file stays aligned with MongoDB's supported configuration.
