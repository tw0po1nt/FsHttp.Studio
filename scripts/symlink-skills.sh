#!/bin/bash
 
# Symlink skills from .agents/skills to .claude/skills.
# This script creates a symbolic link for each skill directory and each skill file.
 
set -e  # Exit on error
 
# Define the source and target directories at the project level.
# Use absolute paths, to avoid problems with symlink resolution.
SOURCE_DIR="$(cd .agents/skills 2>/dev/null && pwd)" || SOURCE_DIR=".agents/skills"
TARGET_DIR="$(pwd)/.claude/skills"
 
# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color
 
# Check whether the source directory exists.
if [ ! -d "$SOURCE_DIR" ]; then
    echo -e "${RED}Error: Source directory '$SOURCE_DIR' does not exist in current project${NC}"
    exit 1
fi
 
# Create the target directory when it does not exist.
if [ ! -d "$TARGET_DIR" ]; then
    echo -e "${YELLOW}Creating target directory: $TARGET_DIR${NC}"
    mkdir -p "$TARGET_DIR"
fi
 
# Counters for the summary.
linked=0
skipped=0
failed=0
 
# Process each skill in the source directory.
for skill in "$SOURCE_DIR"/*; do
    if [ ! -e "$skill" ]; then
        continue
    fi
    
    skill_name=$(basename "$skill")
    target_link="$TARGET_DIR/$skill_name"
    
    # Check whether the target exists already.
    if [ -e "$target_link" ] || [ -L "$target_link" ]; then
        if [ -L "$target_link" ]; then
            # Check whether the symlink points to the correct location.
            current_target=$(readlink "$target_link")
            if [ "$current_target" = "$skill" ]; then
                echo -e "${GREEN}✓${NC} Already linked: $skill_name"
                ((++linked))
                continue
            else
                echo -e "${YELLOW}⚠${NC} Symlink exists but points elsewhere: $skill_name (removing old link)"
                rm "$target_link"
            fi
        else
            echo -e "${YELLOW}⚠${NC} Target exists but is not a symlink: $skill_name (skipping)"
            ((++skipped))
            continue
        fi
    fi
    
    # Create the symlink.
    if ln -s "$skill" "$target_link"; then
        echo -e "${GREEN}✓${NC} Linked: $skill_name"
        ((++linked))
    else
        echo -e "${RED}✗${NC} Failed to link: $skill_name"
        ((++failed))
    fi
done
 
# Summary.
echo ""
echo "================================"
echo -e "Linked:  ${GREEN}$linked${NC}"
echo -e "Skipped: ${YELLOW}$skipped${NC}"
echo -e "Failed:  ${RED}$failed${NC}"
echo "================================"
 
if [ $failed -eq 0 ]; then
    echo -e "${GREEN}All skills symlinked successfully!${NC}"
    exit 0
else
    echo -e "${RED}Some skills failed to link.${NC}"
    exit 1
fi
