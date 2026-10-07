// SPDX-License-Identifier: GPL-3.0-or-later
// Android C ABI bridge for Meltype's Mozc converter.

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <string>
#include <utility>
#include <vector>

#include "absl/strings/str_split.h"
#include "absl/strings/string_view.h"
#include "base/system_util.h"
#include "base/util.h"
#include "composer/composer.h"
#include "config/config_handler.h"
#include "converter/candidate.h"
#include "converter/converter_interface.h"
#include "converter/segments.h"
#include "engine/engine.h"
#include "engine/engine_factory.h"
#include "protocol/commands.pb.h"
#include "protocol/config.pb.h"
#include "request/conversion_request.h"

namespace {

constexpr char kUnitSeparator = '\x1f';
constexpr char kRecordSeparator = '\x1e';

class Helper {
 public:
  explicit Helper(std::unique_ptr<mozc::Engine> engine)
      : engine_(std::move(engine)),
        config_(mozc::config::ConfigHandler::DefaultConfig()),
        converter_(engine_->GetConverter()),
        composer_(request_, config_) {}

  bool Start(absl::string_view context, absl::string_view reading,
             mozc::Segments* segments) {
    if (!context.empty()) {
      (void)converter_->ReconstructHistory(segments, context);
    }
    composer_.Reset();
    composer_.SetPreeditTextForTestOnly(reading);
    return converter_->StartConversion(Request(), segments);
  }

  std::string Convert(absl::string_view context, absl::string_view reading) {
    mozc::Segments segments;
    if (!Start(context, reading, &segments)) return "";

    std::string out;
    for (size_t i = 0; i < segments.conversion_segments_size(); ++i) {
      const mozc::Segment& segment = segments.conversion_segment(i);
      if (i > 0) out += kRecordSeparator;
      out += segment.key();
      for (size_t j = 0; j < segment.candidates_size(); ++j) {
        out += kUnitSeparator;
        out += segment.candidate(j).value;
      }
    }
    return out;
  }

  bool Learn(absl::string_view context, absl::string_view clauses) {
    std::vector<std::pair<std::string, std::string>> chosen;
    std::string reading;
    for (absl::string_view record : absl::StrSplit(clauses, kRecordSeparator)) {
      const std::vector<std::string> fields = absl::StrSplit(record, kUnitSeparator);
      if (fields.size() < 2 || fields[0].empty() || fields[1].empty()) return false;
      reading += fields[0];
      chosen.emplace_back(fields[0], fields[1]);
    }
    if (chosen.empty()) return false;

    mozc::Segments segments;
    if (!Start(context, reading, &segments)) return false;

    std::vector<uint8_t> sizes;
    for (const auto& [key, value] : chosen) {
      const size_t length = mozc::Util::CharsLen(key);
      if (length == 0 || length > 255) return false;
      sizes.push_back(static_cast<uint8_t>(length));
    }

    if (segments.conversion_segments_size() != chosen.size() ||
        !SameKeys(segments, chosen)) {
      if (!converter_->ResizeSegments(&segments, Request(), 0, sizes)) return false;
    }
    if (segments.conversion_segments_size() != chosen.size()) return false;

    for (size_t i = 0; i < chosen.size(); ++i) {
      const mozc::Segment& segment = segments.conversion_segment(i);
      if (segment.key() != chosen[i].first) return false;

      int index = -1;
      for (size_t j = 0; j < segment.candidates_size(); ++j) {
        if (segment.candidate(j).value == chosen[i].second) {
          index = static_cast<int>(j);
          break;
        }
      }
      if (index < 0) return false;
      if (!converter_->CommitSegmentValue(&segments, i, index)) return false;
    }

    converter_->FinishConversion(Request(), &segments);
    return true;
  }

  bool Save() { return engine_->Sync(); }

 private:
  static bool SameKeys(
      const mozc::Segments& segments,
      const std::vector<std::pair<std::string, std::string>>& chosen) {
    if (segments.conversion_segments_size() != chosen.size()) return false;
    for (size_t i = 0; i < chosen.size(); ++i) {
      if (segments.conversion_segment(i).key() != chosen[i].first) return false;
    }
    return true;
  }

  mozc::ConversionRequest Request() {
    mozc::ConversionRequest::Options options = {
        .request_type = mozc::ConversionRequest::CONVERSION,
        .max_conversion_candidates_size = 100,
        .create_partial_candidates = false,
    };
    return mozc::ConversionRequestBuilder()
        .SetComposer(composer_)
        .SetRequestView(request_)
        .SetConfigView(config_)
        .SetOptions(std::move(options))
        .Build();
  }

  std::unique_ptr<mozc::Engine> engine_;
  const mozc::commands::Request request_;
  const mozc::config::Config config_;
  std::shared_ptr<const mozc::ConverterInterface> converter_;
  mozc::composer::Composer composer_;
};

struct Handle {
  explicit Handle(std::unique_ptr<mozc::Engine> engine)
      : helper(std::move(engine)) {}
  Helper helper;
};

char* CopyString(const std::string& value) {
  auto* result = static_cast<char*>(std::malloc(value.size() + 1));
  if (result == nullptr) return nullptr;
  std::memcpy(result, value.data(), value.size());
  result[value.size()] = '\0';
  return result;
}

}  // namespace

#if defined(__GNUC__)
#define MELTYPE_EXPORT __attribute__((visibility("default")))
#else
#define MELTYPE_EXPORT
#endif

extern "C" {

MELTYPE_EXPORT void* meltype_mozc_create(const char* profile_directory) {
  if (profile_directory != nullptr && profile_directory[0] != '\0') {
    mozc::SystemUtil::SetUserProfileDirectory(profile_directory);
  }

  auto engine = mozc::EngineFactory::Create();
  if (!engine.ok()) return nullptr;
  return new Handle(*std::move(engine));
}

MELTYPE_EXPORT void meltype_mozc_destroy(void* handle) {
  if (handle == nullptr) return;
  auto* h = static_cast<Handle*>(handle);
  (void)h->helper.Save();
  delete h;
}

MELTYPE_EXPORT char* meltype_mozc_convert(void* handle, const char* context,
                                         const char* reading) {
  if (handle == nullptr || reading == nullptr || reading[0] == '\0') return nullptr;
  auto* h = static_cast<Handle*>(handle);
  const absl::string_view context_view = context == nullptr ? "" : context;
  return CopyString(h->helper.Convert(context_view, reading));
}

MELTYPE_EXPORT int meltype_mozc_learn(void* handle, const char* context,
                                     const char* clauses) {
  if (handle == nullptr || clauses == nullptr || clauses[0] == '\0') return 0;
  auto* h = static_cast<Handle*>(handle);
  const absl::string_view context_view = context == nullptr ? "" : context;
  return h->helper.Learn(context_view, clauses) ? 1 : 0;
}

MELTYPE_EXPORT int meltype_mozc_save(void* handle) {
  if (handle == nullptr) return 0;
  return static_cast<Handle*>(handle)->helper.Save() ? 1 : 0;
}

MELTYPE_EXPORT void meltype_mozc_free_string(char* value) {
  std::free(value);
}

}  // extern "C"
