<!-- phasepr prompt: draft-body -->
# Write the description of the feature's draft pull request

The feature is `{{FEATURE}}`; its spec is `{{FEATURE_DIR}}/spec.md`. Read that, and
`docs/product.md` for the outcome it advances. You run headless: nobody answers a question.

Write the description of the pull request that will take this feature to `main`. It describes the
**state the feature reaches**, in the present tense, as if it were merged:

- the outcome it advances (`OUT-NN` and its name);
- what the user can do once it is in, told as the user does it;
- the requirement IDs it registers or changes, one line each on what the requirement makes true.

Not the state before, not the diff, not the plan, not the tasks, no phase list: phasepr appends the
phase checklist itself. No heading above the first paragraph — GitHub shows the title.

Change no file and run no `git` or `gh` command. Your whole reply is the Markdown of the
description, with nothing before or after it.
