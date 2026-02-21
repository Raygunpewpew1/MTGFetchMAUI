#!/bin/bash
set -e

# Configuration
# Use current date for unique backup branch name
BACKUP_BRANCH="backup/snapshot-$(date +%Y-%m-%d)"
TARGET_BRANCH="main"
CLEAN_MESSAGE="Fix MTGCardGrid content loss on tab switch"

echo "========================================================"
echo "MTG Fetch MAUI Repository Cleanup Script"
echo "========================================================"
echo "This script will:"
echo "1. Create a safety backup branch ($BACKUP_BRANCH)"
echo "2. Delete ALL remote branches except '$TARGET_BRANCH' and the backup"
echo "3. Clean up the latest commit message on '$TARGET_BRANCH' (removing AI comments)"
echo "4. Force push the clean history to origin"
echo ""
echo "WARNING: This involves a Force Push to '$TARGET_BRANCH'."
echo "Ensure no one else is currently pushing to this branch."
echo ""
read -p "Are you sure you want to proceed? (y/N) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Aborted."
    exit 1
fi

# 1. Safety Backup
echo "--------------------------------------------------------"
echo "Step 1: Creating safety backup..."
echo "--------------------------------------------------------"
git fetch origin $TARGET_BRANCH
git checkout $TARGET_BRANCH
git reset --hard origin/$TARGET_BRANCH
git checkout -b $BACKUP_BRANCH
git push origin $BACKUP_BRANCH
echo "Backup pushed to origin/$BACKUP_BRANCH"

# 2. Delete Stale Remote Branches
echo "--------------------------------------------------------"
echo "Step 2: Deleting stale remote branches..."
echo "--------------------------------------------------------"

# Get list of remote branches
# --format="%(refname:short)" gives 'origin/branchname'
# grep -v excludes main, HEAD, and our new backup
BRANCHES_TO_DELETE=$(git branch -r --format="%(refname:short)" | \
    grep -v "^origin/HEAD$" | \
    grep -v "^origin/$TARGET_BRANCH$" | \
    grep -v "^origin/$BACKUP_BRANCH" | \
    sed 's/^origin\///')

if [ -z "$BRANCHES_TO_DELETE" ]; then
    echo "No stale remote branches found to delete."
else
    echo "Deleting the following branches:"
    echo "$BRANCHES_TO_DELETE"
    echo ""

    # Convert newlines to spaces for git push arguments
    BRANCH_LIST=$(echo "$BRANCHES_TO_DELETE" | tr '\n' ' ')

    # Batch delete
    git push origin --delete $BRANCH_LIST || echo "Warning: Some branches might not have been deleted."
fi

# 3. Clean Up Commit History
echo "--------------------------------------------------------"
echo "Step 3: Cleaning up commit history on $TARGET_BRANCH..."
echo "--------------------------------------------------------"
git checkout $TARGET_BRANCH

# Amend the commit message
# Note: This changes the commit hash!
git commit --amend -m "$CLEAN_MESSAGE"

# 4. Force Push
echo "--------------------------------------------------------"
echo "Step 4: Force pushing cleaned history..."
echo "--------------------------------------------------------"
git push --force origin $TARGET_BRANCH

echo "========================================================"
echo "Cleanup Complete!"
echo "Your repo is now clean."
echo "If anything went wrong, your code is safe in '$BACKUP_BRANCH'."
echo "========================================================"
