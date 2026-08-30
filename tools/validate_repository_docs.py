from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlparse

try:
    import yaml
except ImportError:
    print("ERROR: PyYAML is required to validate GitHub Issue Forms.", file=sys.stderr)
    raise SystemExit(2)


ROOT = Path(__file__).resolve().parents[1]
ERRORS: list[str] = []
LINK_PATTERN = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
EXCLUDED_PARTS = {".git", "artifacts", "bin", "obj", "回收站"}


def fail(message: str) -> None:
    ERRORS.append(message)


def repository_markdown_files() -> list[Path]:
    return sorted(
        path
        for path in ROOT.rglob("*.md")
        if not any(part in EXCLUDED_PARTS for part in path.parts)
    )


def validate_markdown_links() -> int:
    checked = 0
    for markdown in repository_markdown_files():
        text = markdown.read_text(encoding="utf-8")
        for match in LINK_PATTERN.finditer(text):
            raw_target = match.group(1).strip()
            if not raw_target or raw_target.startswith("#"):
                continue
            target_token = raw_target.split(maxsplit=1)[0].strip("<>")
            parsed = urlparse(target_token)
            if parsed.scheme in {"http", "https", "mailto"}:
                continue
            relative_path = unquote(target_token.split("#", 1)[0])
            if not relative_path:
                continue
            checked += 1
            resolved = (markdown.parent / relative_path).resolve()
            try:
                resolved.relative_to(ROOT.resolve())
            except ValueError:
                fail(f"Markdown link escapes repository: {markdown.relative_to(ROOT)} -> {target_token}")
                continue
            if not resolved.exists():
                fail(f"Broken Markdown link: {markdown.relative_to(ROOT)} -> {target_token}")
    return checked


def validate_issue_forms() -> int:
    forms_dir = ROOT / ".github" / "ISSUE_TEMPLATE"
    expected = {
        "bug_report.yml",
        "feature_request.yml",
        "file_safety_recovery.yml",
        "compatibility.yml",
    }
    actual = {path.name for path in forms_dir.glob("*.yml") if path.name != "config.yml"}
    if actual != expected:
        fail(f"Issue Form set mismatch: expected {sorted(expected)}, actual {sorted(actual)}")

    validated = 0
    allowed_types = {"markdown", "input", "textarea", "dropdown", "checkboxes"}
    for name in sorted(expected):
        path = forms_dir / name
        try:
            data = yaml.safe_load(path.read_text(encoding="utf-8"))
        except Exception as exc:
            fail(f"Invalid YAML: {path.relative_to(ROOT)}: {exc}")
            continue
        if not isinstance(data, dict):
            fail(f"Issue Form root must be a mapping: {name}")
            continue
        if not isinstance(data.get("name"), str) or not isinstance(data.get("description"), str):
            fail(f"Issue Form requires name and description: {name}")
        body = data.get("body")
        if not isinstance(body, list) or not body:
            fail(f"Issue Form body must be a non-empty list: {name}")
            continue
        ids: set[str] = set()
        for index, item in enumerate(body):
            if not isinstance(item, dict) or item.get("type") not in allowed_types:
                fail(f"Invalid Issue Form item type: {name} body[{index}]")
                continue
            if item["type"] != "markdown":
                item_id = item.get("id")
                if not isinstance(item_id, str) or not item_id:
                    fail(f"Interactive Issue Form item requires id: {name} body[{index}]")
                elif item_id in ids:
                    fail(f"Duplicate Issue Form id: {name}: {item_id}")
                else:
                    ids.add(item_id)
            if not isinstance(item.get("attributes"), dict):
                fail(f"Issue Form item requires attributes: {name} body[{index}]")
        validated += 1

    config_path = forms_dir / "config.yml"
    try:
        config = yaml.safe_load(config_path.read_text(encoding="utf-8"))
        if config.get("blank_issues_enabled") is not False:
            fail("Issue template config must disable blank issues.")
        if len(config.get("contact_links", [])) < 3:
            fail("Issue template config requires Discussions, Security, and Support links.")
    except Exception as exc:
        fail(f"Invalid YAML: {config_path.relative_to(ROOT)}: {exc}")
    return validated


def validate_claims_and_statuses() -> None:
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    required_readme = [
        "easy重命名 / BatchRenamer",
        "A modern, safe and extensible",
        "batch renaming toolkit for Windows.",
        "Download for Windows",
        "20,000 real-file transaction/undo stress tested",
        "SCREENSHOT_STATUS = PENDING",
        "DEMO_GIF_STATUS = PENDING",
        "ICON_ASSET_STATUS = PENDING",
    ]
    for phrase in required_readme:
        if phrase not in readme:
            fail(f"README required phrase missing: {phrase}")

    forbidden_current_claims = [
        r"已支持\s*Regex",
        r"已支持\s*Plugin",
        r"已支持\s*CLI",
        r"AI-powered renaming",
        r"Regex support is available",
        r"Plugin SDK is available",
    ]
    for pattern in forbidden_current_claims:
        if re.search(pattern, readme, flags=re.IGNORECASE):
            fail(f"README presents planned work as implemented: {pattern}")

    roadmap_path = ROOT / "docs" / "ROADMAP.md"
    roadmap = roadmap_path.read_text(encoding="utf-8")
    required_versions = [
        "v1.0 — Core Product（Current）",
        "v1.1 — Power User（Planned）",
        "v1.2 — Personalization（Planned）",
        "v1.3 — Automation（Planned）",
        "v1.5 — Extension Platform（Planned）",
        "v2.0 — Intelligent Renaming（Planned）",
    ]
    for version in required_versions:
        if version not in roadmap:
            fail(f"Roadmap stage missing: {version}")

    future_terms = {
        "Template Engine",
        "Regex",
        "Rule Chain",
        "Rule Scope",
        "Grouped Sequence",
        "Recursive Folder",
        "Dark Mode",
        "CLI",
        "Plugin SDK",
    }
    planned_section = False
    for line_number, line in enumerate(roadmap.splitlines(), start=1):
        if line.startswith("## "):
            planned_section = "（Planned）" in line
        if any(term in line for term in future_terms) and not planned_section:
            fail(f"Future capability is outside a Planned section: docs/ROADMAP.md:{line_number}")


def validate_privacy_wording() -> None:
    required_fragments = {
        ROOT / "SUPPORT.md": ["不要上传私人文件", "不要公开敏感文件名", "不要继续随意移动相关文件"],
        ROOT / "SECURITY.md": ["Private vulnerability reporting", "不要上传私人文件", "不要公开敏感文件名或路径"],
        ROOT / ".github" / "ISSUE_TEMPLATE" / "file_safety_recovery.yml": [
            "不要上传私人文件",
            "不要公开敏感文件名/路径",
            "不要继续随意移动相关文件",
            "不要删除事务目录或日志",
        ],
    }
    for path, fragments in required_fragments.items():
        text = path.read_text(encoding="utf-8")
        for fragment in fragments:
            if fragment not in text:
                fail(f"Privacy/safety wording missing in {path.relative_to(ROOT)}: {fragment}")


def main() -> int:
    required_files = [
        "README.md",
        "SUPPORT.md",
        "SECURITY.md",
        "CONTRIBUTING.md",
        "docs/ROADMAP.md",
        "docs/SAFETY_ARCHITECTURE.md",
        ".github/pull_request_template.md",
    ]
    for relative in required_files:
        if not (ROOT / relative).is_file():
            fail(f"Required repository document missing: {relative}")

    link_count = validate_markdown_links()
    form_count = validate_issue_forms()
    validate_claims_and_statuses()
    validate_privacy_wording()

    if ERRORS:
        for error in ERRORS:
            print(f"FAIL: {error}", file=sys.stderr)
        print(f"REPOSITORY_DOCS=FAIL ({len(ERRORS)} errors)", file=sys.stderr)
        return 1

    print(f"MARKDOWN_LINKS=PASS ({link_count} local links)")
    print(f"ISSUE_FORM_YAML=PASS ({form_count} forms)")
    print("IMPLEMENTED_VS_ROADMAP=PASS")
    print("PRIVACY_WORDING=PASS")
    print("REPOSITORY_DOCS=PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
