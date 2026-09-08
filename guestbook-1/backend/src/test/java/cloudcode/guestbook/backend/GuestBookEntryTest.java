package cloudcode.guestbook.backend;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

class GuestBookEntryTest {

    @Test
    void storesAndReturnsEntryFields() {
        GuestBookEntry entry = new GuestBookEntry();

        entry.setAuthor("Ada");
        entry.setMessage("Hello");
        entry.setDate(123L);

        assertEquals("Ada", entry.getAuthor());
        assertEquals("Hello", entry.getMessage());
        assertEquals(123L, entry.getDate());
    }
}
