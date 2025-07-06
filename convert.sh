#!/bin/bash

# 바꾸고자 하는 루트 디렉토리 (예시: ./src)
TARGET_DIR="./"

# 모든 .java 파일 찾아서 확장자만 .cs로 변경
find "$TARGET_DIR" -type f -name "*.java" | while read file; do
  newfile="${file%.java}.cs"
  mv "$file" "$newfile"
  echo "Renamed: $file → $newfile"
done

