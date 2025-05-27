# Changelog
|Version|Commit|Notes|
|-|-|-|
|0.3.0|`c752f6ddef7e9ba44dbd68e817da6702bed9acc1`||
|0.4.0|`f4b6bf486a48db051418e1f5fccd2c7154f0c537`|Added FinanceGuru with stock price checking ability, added ability to read PDF's|
|0.5.0|`7650e0822850b409ac96a0648cb7cdbad1fae7bf`|Added `help` command, `tools` command. Removed HTTP call timeout.|
|0.5.1|`3ba8528a61431c310a5d1f568cef167f9cc9eb09`|Fixed an issue where tool call requests were being cut while their tool call response pair were not, causing an error.|
|0.6.0|`f25be1020c51b27958ab3827f90314cd01358512`|Added additional config command data and fixed error with Bing Search API key having multiple lines.|
|0.7.0|`fc3c3a16b4c247c8eaca79776788592c34d84e83`|Ability to clear chat history, display of search term and page reading, minor system prompt tweak|
|0.8.0|`6563749b6b79adcd9dbcbf25d18b57b44dc84e68`|Removed `web_search` tool (Bing Search API now deprecated), markdown outputs are now presented as formatted, tweaked `tokens` command response and cost estimate, and display AI messages in a standard color| 
|0.9.0|`0ebf72fb65744da3953cbb562de2badcd4eb7d66`|Removed debugging printing on header markdown conversion and changed AI output color to navy blue|
|0.9.1|`a02b0e5f37e843fc6245c227e537b8c4da41dd29`|Fixed issue with markdown bullet points not printing correctly|