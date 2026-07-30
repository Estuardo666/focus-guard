# Codex overnight prompt

You are operating in a local FocusGuard repository.

Read `AGENTS.md`, `SPEC.md`, `UX_SPEC.md`, `THREAT_MODEL.md`, `AUTOPILOT.md`, `TASKS.md`, and every ADR before changing files.

Execute the safe overnight milestones in `TASKS.md` sequentially.

Rules:

- Follow the loop and stop conditions in `AUTOPILOT.md`.
- Do not perform administrator-level operations.
- Do not install a Windows Service.
- Do not terminate real processes.
- Do not modify Registry, firewall, hosts file, browser policy, scheduled tasks, boot settings, or machine ACLs.
- Do not implement covert persistence or interference with security/recovery tools.
- Keep all enforcement interfaces simulated until owner-present milestones.
- Build modern Avalonia screens using semantic theme tokens.
- Add accessible labels and keyboard behavior.
- Add tests before claiming features complete.
- Run build and tests after each milestone.
- Update `TASKS.md` and create progress records.
- If a task is blocked, document it and continue only with an independent safe task.
- After the safe queue ends or a stop condition occurs, create `OVERNIGHT_REPORT.md`.

Begin with FG-000. Do not ask for confirmation unless continuing would violate a stop condition.
