#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
프로젝트 이름 변경 스크립트: Nebula -> TermSnap
모든 코드, XAML, 설정 파일에서 Nebula 참조를 TermSnap으로 변경
"""
import os
import re
from pathlib import Path

# 프로젝트 루트 디렉토리
ROOT_DIR = Path(__file__).parent
SRC_DIR = ROOT_DIR / "src"

def replace_in_file(file_path, replacements):
    """파일 내용을 변경"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()

        original_content = content
        for old, new in replacements:
            content = content.replace(old, new)

        if content != original_content:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(content)
            return True
        return False
    except Exception as e:
        print(f"Error processing {file_path}: {e}")
        return False

def main():
    # 변경할 패턴 (순서 중요: 더 긴 패턴부터)
    replacements = [
        # Namespace
        ('namespace Nebula', 'namespace TermSnap'),
        ('using Nebula', 'using TermSnap'),

        # XAML namespace
        ('xmlns:local="clr-namespace:Nebula', 'xmlns:local="clr-namespace:TermSnap'),

        # Pack URI (XAML 리소스 경로)
        ('pack://application:,,,/Nebula;component', 'pack://application:,,,/TermSnap;component'),

        # 문자열 리터럴
        ('"Nebula Terminal"', '"TermSnap"'),
        ("'Nebula Terminal'", "'TermSnap'"),
        ('"Nebula"', '"TermSnap"'),
        ("'Nebula'", "'TermSnap'"),

        # IPC pipe name
        ('Nebula_MCP', 'TermSnap_MCP'),

        # Namespace prefix
        ('Nebula.', 'TermSnap.'),

        # 경로 (Windows)
        ('\\Nebula\\', '\\TermSnap\\'),

        # 경로 (Unix)
        ('/Nebula/', '/TermSnap/'),

        # 주석
        ('# Nebula', '# TermSnap'),
        ('// Nebula', '// TermSnap'),
    ]

    # C# 파일 처리
    cs_files = list(SRC_DIR.rglob('*.cs'))
    print(f"Processing {len(cs_files)} C# files...")
    cs_changed = 0
    for cs_file in cs_files:
        if replace_in_file(cs_file, replacements):
            cs_changed += 1
            print(f"  [OK] {cs_file.relative_to(ROOT_DIR)}")

    # XAML 파일 처리
    xaml_files = list(SRC_DIR.rglob('*.xaml'))
    print(f"\nProcessing {len(xaml_files)} XAML files...")
    xaml_changed = 0
    for xaml_file in xaml_files:
        if replace_in_file(xaml_file, replacements):
            xaml_changed += 1
            print(f"  [OK] {xaml_file.relative_to(ROOT_DIR)}")

    # 기타 설정 파일 처리
    other_files = []
    for pattern in ['*.json', '*.config', '*.xml', '*.md']:
        other_files.extend(ROOT_DIR.rglob(pattern))

    print(f"\nProcessing {len(other_files)} other files...")
    other_changed = 0
    for other_file in other_files:
        if replace_in_file(other_file, replacements):
            other_changed += 1
            print(f"  [OK] {other_file.relative_to(ROOT_DIR)}")

    print(f"\n{'='*60}")
    print(f"[DONE] Nebula -> TermSnap conversion complete!")
    print(f"{'='*60}")
    print(f"   C# files changed: {cs_changed}/{len(cs_files)}")
    print(f"   XAML files changed: {xaml_changed}/{len(xaml_files)}")
    print(f"   Other files changed: {other_changed}/{len(other_files)}")
    print(f"   Total: {cs_changed + xaml_changed + other_changed} files")

if __name__ == '__main__':
    main()
