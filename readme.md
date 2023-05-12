Just start the exe and look whats happening - it will generate 2 files:



**versioninfo.json** containing a json structure with extracted metadata

**changelog.md** which can be automatically maintained by re-running the exe



>  You can edit the entries under *Upcoming Changes* heading manually and you can use the following key-words:
>
> ```
> **MVP**            //will initate transition from 0.x.x to 1.0.0 (must be BOLD!!!)
> breaking Change    //will initiate a major-version jump
> new Feature        //will initiate a minor-version jump
> ```
>
> Hard coded and required headings (please leave them as they are!):
>
> ```
> # Change log
> ## Upcomming Changes
> ## v <VERSION>
> ```


