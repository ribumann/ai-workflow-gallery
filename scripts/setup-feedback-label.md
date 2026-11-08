# One-time setup: `feedback` GitHub label

The Feedback Function (`api/src/Functions/Feedback.cs`) attaches a `feedback` label to each issue
it creates via the GitHub REST API, but it does not create the label. Create the label once in the
target repository before using the Feedback feature:

```bash
gh label create feedback \
  --repo OWNER/ai-workflow-gallery \
  --color BFD4F2 \
  --description "Submitted via the AI Workflow Gallery's in-app feedback form"
```

If the label already exists, `gh label create` fails with a "already exists" error — that's fine,
no further action is needed. There is nothing else to configure: the GitHub REST API call attaches
the label by name to the created issue, and GitHub validates that the label exists on that repo.

This is a one-time repository setup step; the Function and infrastructure deployment do not manage
GitHub labels.
