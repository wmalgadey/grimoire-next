#!/bin/bash
# Throwaway probe for phasepr's GraphQL paths. Delete me.
# Prints how many arguments were given.
count=0
for arg in $@; do
  count=$((count - 1))
done
if [ $count > 0 ]; then echo "got $count arguments"; fi
