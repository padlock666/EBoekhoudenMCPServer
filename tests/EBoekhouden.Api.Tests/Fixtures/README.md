# Synthetic API fixtures

Every JSON file in this directory is invented test data, embedded into the test assembly. None is a captured live response, an actual financial record, or a credential. `.invalid` domains are deliberate examples. These fixtures prove local behavior against the supplied specification and documented compatibility policies; they do not verify the live API.

List/detail files cover the eleven required GET routes, both casing styles, null/absent optional fields, additive fields, decimals, DateOnly dates and unknown response codes. Invoice item quantity and deprecated amount intentionally differ, and one item has only amount. Error fixtures intentionally include unsafe free text and differing errors-collection shapes; only bounded sanitized code/trace metadata may escape. The dictionary error fixture's body status deliberately disagrees with HTTP status to verify that HTTP is authoritative.
