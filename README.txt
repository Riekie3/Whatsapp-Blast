WHATSAPP BLAST - one picture per row, with a wait between each chat
=====================================================================

FIRST TIME ONLY
  Double-click 1-INSTALL.bat (needs internet, takes a minute).

LET THE TOOL USE YOUR CHROME
  In the Chrome that is logged in to WhatsApp Web, type
  chrome://inspect/#remote-debugging in the address bar and tick
  "Allow remote debugging for this browser instance".
  While it is ticked, programs on this PC can ask to control that Chrome,
  so untick it when the blast is finished.

EVERY BLAST
  1. Put your pictures in the "pics" folder.
  2. Fill in contacts.csv (new setup: copy contacts-template.csv and rename
     the copy to contacts.csv). You can also save an Excel file as
     contacts.xlsx here - if it exists it is used instead of contacts.csv.
     Columns (row 1 must be these headers):
       phone    e.g. 0123456789 or 60123456789 (dashes/spaces are fine)
       image    the picture's file name in the pics folder, e.g. ali.jpg
       caption  optional text sent with the picture
  3. Double-click 2-CHECK-LIST.bat - it lists any bad numbers or missing
     pictures and tells you how long the blast will take. Fix and re-check.
  4. Close any WhatsApp Web tab you have open, then click your logged-in
     Chrome window once (so the right Chrome profile is used).
     Double-click 3-SEND.bat and click "Allow" if Chrome asks. A new
     WhatsApp tab opens in your Chrome and sending starts.
     Leave that tab and the black window open until it says "Finished".
     The tool then closes its own tab; the rest of Chrome stays open.

RESULTS
  sent-log.csv lists each name (the picture's file name) and number with its
  result: SENT / NOT_ON_WHATSAPP / FAILED / SKIPPED.
  If you run 3-SEND.bat again, a picture already SENT to a number is skipped,
  so it is safe to re-run after a stop or crash.
  One number can get two different pictures: give each its own row.
  For a brand-new blast to the same people, delete or rename sent-log.csv.

CHANGE THE WAIT TIME (now 30 seconds between chats)
  1. Right-click send.js > Open with > Notepad.
     (Do not double-click send.js.)
  2. Near the top, under "Settings", find this line:
       const GAP_SECONDS = 30; // wait between each chat
  3. Change only the number. The unit is seconds, e.g. 45 or 60.
     Keep the rest of the line, including the ; sign.
  4. Save (Ctrl+S) and close Notepad.
  5. Double-click 2-CHECK-LIST.bat to see the new total time.
  If a blast is already running, the new time applies only after you close it
  and run 3-SEND.bat again (people already SENT are skipped).
  A shorter wait raises the risk of WhatsApp banning the number.

LOG OUT / USE ANOTHER NUMBER
  The tool sends from whichever WhatsApp your Chrome is logged in to.
  Change the login in WhatsApp Web itself.

SEPARATE CHROME WINDOW INSTEAD
  In send.js set  const USE_MY_CHROME = false;
  Note (27 Nov 2026): the QR login in a separate window fails at present
  because WhatsApp changed its login page.

AVOID A BAN
  - Only message people who know you.
  - New number: start with 30-50 a day, then increase slowly.
  - Put the person's name in each caption so messages are not identical.
