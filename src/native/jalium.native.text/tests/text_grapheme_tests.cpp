#include "text_grapheme.h"
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
int main(int argc, char** argv)
{
    if (argc != 2) return 1;
    std::ifstream input(argv[1]); if (!input) return 1;
    unsigned count = 0, failures = 0;
    for (std::string line; std::getline(input, line);) {
        line = line.substr(0, line.find('#'));
        std::istringstream fields(line); std::string token; std::wstring text;
        std::vector<uint32_t> expected;
        while (fields >> token) {
            if (token == "÷") expected.push_back(static_cast<uint32_t>(text.size()));
            else if (token != "×") {
                uint32_t cp = std::stoul(token, nullptr, 16);
                if constexpr (sizeof(wchar_t) == 2) {
                    if (cp > 0xFFFF) { text.push_back(wchar_t(0xD800 + ((cp - 0x10000) >> 10))); text.push_back(wchar_t(0xDC00 + ((cp - 0x10000) & 1023))); continue; }
                }
                text.push_back(wchar_t(cp));
            }
        }
        if (expected.empty()) continue;
        ++count;
        if (jalium::grapheme::Boundaries(text) != expected) { ++failures; std::cerr << "GraphemeBreakTest mismatch " << count << '\n'; }
    }
    std::wstring longMarks(100000, wchar_t(0x0301)); longMarks.insert(0, 1, L'A');
    if (jalium::grapheme::Boundaries(longMarks) != std::vector<uint32_t>{0, 100001}) ++failures;
    std::cout << "{\"count\":" << count << ",\"failed\":" << failures << "}\n";
    return failures ? 1 : 0;
}
