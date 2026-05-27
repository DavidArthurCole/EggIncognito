require 'minitest/autorun'
require 'tmpdir'
require_relative 'server'

EID_PROTO = 'MhJFSTAwMDAwMDAwMDAwMDAwMDE='

class TestExtractEid < Minitest::Test
  def test_extract_eid
    assert_equal 'EI0000000000000001', extract_eid(EID_PROTO)
  end

  def test_extract_eid_empty
    assert_equal '', extract_eid('')
  end
end

class TestLoadFixture < Minitest::Test
  def setup
    @dir = Dir.mktmpdir('ei-test-')
    @orig = FIXTURES_PATH
  end

  def teardown
    FileUtils.rm_rf(@dir)
    silence_warnings { Object.const_set(:FIXTURES_PATH, @orig) }
  end

  def test_load_fixture_default
    FileUtils.mkdir_p(File.join(@dir, 'default'))
    File.binwrite(File.join(@dir, 'default', 'test.binpb'), "\x01\x02")
    silence_warnings { Object.const_set(:FIXTURES_PATH, @dir) }
    assert_equal "\x01\x02", load_fixture('test', '')
  end

  def test_load_fixture_eid_override
    FileUtils.mkdir_p(File.join(@dir, 'default'))
    FileUtils.mkdir_p(File.join(@dir, 'eids', 'EI0000000000000001'))
    File.binwrite(File.join(@dir, 'default', 'test.binpb'), "\x01")
    File.binwrite(File.join(@dir, 'eids', 'EI0000000000000001', 'test.binpb'), "\x02")
    silence_warnings { Object.const_set(:FIXTURES_PATH, @dir) }
    assert_equal "\x02", load_fixture('test', 'EI0000000000000001')
  end

  def test_load_fixture_missing
    silence_warnings { Object.const_set(:FIXTURES_PATH, @dir) }
    assert_equal '', load_fixture('nonexistent', '')
  end

  private

  def silence_warnings
    old = $VERBOSE
    $VERBOSE = nil
    yield
  ensure
    $VERBOSE = old
  end
end
