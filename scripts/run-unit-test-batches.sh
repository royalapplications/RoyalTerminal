#!/usr/bin/env bash

set -euo pipefail

project="${ROYALTERMINAL_TEST_PROJECT:-tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj}"
configuration="${ROYALTERMINAL_TEST_CONFIGURATION:-Release}"
results_dir="${ROYALTERMINAL_TEST_RESULTS_DIR:-test-results}"
batch_target="${ROYALTERMINAL_TEST_BATCH_TARGET:-40}"
validate_coverage="${ROYALTERMINAL_VALIDATE_TEST_BATCH_COVERAGE:-false}"
max_duplicate_matches="${ROYALTERMINAL_TEST_MAX_DUPLICATE_MATCHES:-16}"
blame_crash="${ROYALTERMINAL_TEST_BLAME_CRASH:-false}"
blame_hang="${ROYALTERMINAL_TEST_BLAME_HANG:-false}"
blame_hang_timeout="${ROYALTERMINAL_TEST_BLAME_HANG_TIMEOUT:-5m}"
blame_hang_dump_type="${ROYALTERMINAL_TEST_BLAME_HANG_DUMP_TYPE:-}"

if ! [[ "${batch_target}" =~ ^[1-9][0-9]*$ ]]; then
  echo "::error::ROYALTERMINAL_TEST_BATCH_TARGET must be a positive integer, got '${batch_target}'."
  exit 1
fi

if ! [[ "${max_duplicate_matches}" =~ ^[0-9]+$ ]]; then
  echo "::error::ROYALTERMINAL_TEST_MAX_DUPLICATE_MATCHES must be a non-negative integer, got '${max_duplicate_matches}'."
  exit 1
fi

mkdir -p "${results_dir}"

tmp_dir="$(mktemp -d)"
trap 'rm -rf "${tmp_dir}"' EXIT

all_tests="${tmp_dir}/all-tests.txt"
methods="${tmp_dir}/methods.tsv"
batches="${tmp_dir}/batches.tsv"
matched_tests="${tmp_dir}/matched-tests.txt"
matched_tests_sorted="${tmp_dir}/matched-tests-sorted.txt"
matched_tests_unique="${tmp_dir}/matched-tests-unique.txt"
uncovered_tests="${tmp_dir}/uncovered-tests.txt"
unexpected_tests="${tmp_dir}/unexpected-tests.txt"
duplicate_tests="${tmp_dir}/duplicate-tests.txt"
batch_tests="${tmp_dir}/batch-tests.txt"

extract_listed_tests() {
  awk '
    /^[[:space:]]{4}[^[:space:]]/ {
      sub(/^[[:space:]]+/, "")
      print
    }
  '
}

dotnet test "${project}" \
  -c "${configuration}" \
  --no-build \
  --list-tests |
  extract_listed_tests |
  sort -u > "${all_tests}"

if [ ! -s "${all_tests}" ]; then
  echo "::error::No unit tests were discovered for ${project}."
  exit 1
fi

awk '
  {
    method = $0
    sub(/\(.*/, "", method)
    print method
  }
' "${all_tests}" |
  sort |
  uniq -c |
  awk '
    {
      count = $1
      $1 = ""
      sub(/^[[:space:]]+/, "")
      printf "%d\t%s\n", count, $0
    }
  ' > "${methods}"

awk -F '\t' -v target="${batch_target}" '
  function flush_batch() {
    if (filter == "") {
      return
    }

    printf "%d\t%d\t%s\n", batch, count, filter
    batch++
    count = 0
    filter = ""
  }

  BEGIN {
    batch = 1
  }

  {
    method_count = $1 + 0
    method_name = $2
    # Exact method filters keep theory rows together without substring overlap.
    segment = "(FullyQualifiedName=" method_name ")"

    if (filter != "" && count + method_count > target) {
      flush_batch()
    }

    if (filter == "") {
      filter = segment
    } else {
      filter = filter "|" segment
    }

    count += method_count
  }

  END {
    flush_batch()
  }
' "${methods}" > "${batches}"

if [ ! -s "${batches}" ]; then
  echo "::error::No unit test batches were generated for ${project}."
  exit 1
fi

echo "Discovered $(wc -l < "${all_tests}" | tr -d ' ') unit tests across $(wc -l < "${methods}" | tr -d ' ') test methods."
echo "Generated $(wc -l < "${batches}" | tr -d ' ') unit test batches with target size ${batch_target}."

if [ "${validate_coverage}" = "true" ]; then
  : > "${matched_tests}"

  while IFS=$'\t' read -r batch estimated_count filter; do
    dotnet test "${project}" \
      -c "${configuration}" \
      --no-build \
      --list-tests \
      --filter "${filter}" |
      extract_listed_tests |
      sort -u > "${batch_tests}"

    if [ ! -s "${batch_tests}" ]; then
      echo "::error::Generated unit test batch ${batch} does not match any discovered tests."
      echo "Filter: ${filter}"
      exit 1
    fi

    cat "${batch_tests}" >> "${matched_tests}"
  done < "${batches}"

  sort "${matched_tests}" > "${matched_tests_sorted}"
  sort -u "${matched_tests_sorted}" > "${matched_tests_unique}"
  uniq -d "${matched_tests_sorted}" > "${duplicate_tests}"
  comm -23 "${all_tests}" "${matched_tests_unique}" > "${uncovered_tests}"
  comm -13 "${all_tests}" "${matched_tests_unique}" > "${unexpected_tests}"

  if [ -s "${duplicate_tests}" ]; then
    duplicate_count="$(wc -l < "${duplicate_tests}" | tr -d ' ')"
    if [ "${duplicate_count}" -gt "${max_duplicate_matches}" ]; then
      echo "::error::Generated unit test batch filters overlap by ${duplicate_count} tests, exceeding ROYALTERMINAL_TEST_MAX_DUPLICATE_MATCHES=${max_duplicate_matches}."
      sed -n '1,200p' "${duplicate_tests}"
      exit 1
    fi

    echo "::warning::Generated unit test batch filters overlap by ${duplicate_count} tests. This is below ROYALTERMINAL_TEST_MAX_DUPLICATE_MATCHES=${max_duplicate_matches}."
    sed -n '1,200p' "${duplicate_tests}"
  fi

  if [ -s "${uncovered_tests}" ]; then
    echo "::error::Generated unit test batches do not cover every discovered test."
    sed -n '1,200p' "${uncovered_tests}"
    exit 1
  fi

  if [ -s "${unexpected_tests}" ]; then
    echo "::error::Generated unit test batches match tests outside the discovered set."
    sed -n '1,200p' "${unexpected_tests}"
    exit 1
  fi
fi

while IFS=$'\t' read -r batch estimated_count filter; do
  name="$(printf "test-results-unit-batch-%03d" "${batch}")"
  test_args=(
    dotnet test "${project}"
    -c "${configuration}"
    --no-build
    --filter "${filter}"
    --diag "${results_dir}/${name}.diag.log"
    --logger "trx;LogFileName=${name}.trx"
    --results-directory "${results_dir}"
  )

  if [ "${blame_crash}" = "true" ]; then
    test_args+=(--blame-crash)
  fi

  if [ "${blame_hang}" = "true" ]; then
    test_args+=(--blame-hang --blame-hang-timeout "${blame_hang_timeout}")
    if [ -n "${blame_hang_dump_type}" ]; then
      test_args+=(--blame-hang-dump-type "${blame_hang_dump_type}")
    fi
  fi

  echo "::group::Unit test batch ${batch} (${estimated_count} discovered tests)"
  "${test_args[@]}"
  echo "::endgroup::"
done < "${batches}"
