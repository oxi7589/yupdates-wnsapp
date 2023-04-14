# /y/updates crawler (WnsApp)

WnsApp is extensible cloud drives updates tracker that used to power the now obsolete /y/updates. It was used to crawl public cloud folders and compile a report of the additions.

*Note that due to the nature of its original use cases links contained within the source code and the documentation may lead to resources containing pornographic or otherwise offensive content.*


## Usage

WnsApp has no binary dependencies other than the NuGet packages specified in the project file, and should run on anything that is compatible with .NET Framework 4.5, including Mono.

Initial setup is done via placing the configuration files (see "File inputs" section for a list) in the same directory as the WnsApp executable.

The simplest use case involves running WnsApp and then copying the resulting report to a directory served by an HTTP(s) server:

    /usr/bin/mono ./WhatsNewShared.exe
    cp ~/.wnsapp/index.html /var/www/default/html/index.html


## Maintenance status

**END OF LIFE**

WnsApp has been mostly functional at the moment of the initial source publication, but no future support should be expected. Furthermore, it is still rather tightly coupled to a few other projects (such as having an YDC integration). Ideally if you want to use it in your workflow you should create a fork and remove all the parts you don't need.

Note: RSS/Atom feed functionality has not been used in recent versions and may not work properly.

## Command line args

- `-nolimit`  
	allow report to grow as large as it wants
	
- `-rpX`  
	X (positive integer) - the number of days to consider recent enough
	default is 32
	
- `-push2ydc`  
	DO NOT USE  
	used to push the resulting report to the YDC, but this is no longer supported


## Universal handler arguments

Template:

    MEGA blah blah blah << arg1 arg2
    ^ handler                         ^ universal args
              ^ handler args

- `NotBefore $long`  
	$long is number of ticks, .NET DateTime style  
	cuts off all records made before the specified time

- `Mirror $path`  
	links to the mirror instead of the underlying drive  
	$path is NOT /-terminated, for example: `http://uwu.local/artist`

## File inputs

Sample configuration is provided in the `sample-config` directory; note that `roots.txt` **does** require modifications before it can be used, and that you should create your own `client_id.json` if Google Drive support is required.

`()` means optional  
`~` means /home/$username on Unix, and Documents library on Windows

`ExePath/roots.txt`		List of drives to check (see "Universal handler arguments" section)  
`ExePath/header.txt`		Stuff that goes before the report body in the exported latest.htm  
`ExePath/footer.txt`		Stuff that goes after the report body in the exported latest.htm  
`ExePath/ydc-secret.txt`		A secret string matching that in the YDC's config.py (A-Za-z0-9-). Leave empty since this is no longer supported.

`ExePath/client_id.json`		Google Drive API client ID  
 (`~/.wnsapp/gdrive.json` FOLDER)	A folder with Google Drive auth tokens.  
> If not present, will attempt to spawn a webbrowser, which is terrible  
on a headless box. Better login on Windows, then copy this folder.

`ExePath/atombody.xml`		Atom feed body template  
`ExePath/atomentry.xml`		Atom feed entry template

## File outputs

`~/.wnsapp/latest.htm`		Latest report  
`~/.wnsapp/latest.bak.htm`	Previous report (made by copying latest.htm before overwriting)  
`~/.wnsapp/latest.json`		Latest report in JSON format, primarily for YDC publication

`~/.wnsapp/recordslist.wnsm`	Difference between this and previous reports as an HTML fragment (this is what we've used to push to the Discord bot after converting it to Markdown)  
`~/.wnsapp/latest.atom.xml`	Atom feed with the latest 10 or so useful diffs.

#### internal use:

`~/.wnsapp/prevrep.json`		Previous report in intermediate format, used to calculate the difference and to preserve the order (newly found on top, even if the date is not quite as fresh)

`~/.wnsapp/recordslist.json`	Previous report in form of a json array of html pieces (used exclusively for the Atom feed)  
`~/.wnsapp/recordslist.atom.json`	Intermediate representation of the previous Atom feed

## License

MIT